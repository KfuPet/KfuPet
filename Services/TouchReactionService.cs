using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 触摸反应服务：汇总触摸事件所需的两类文案。
    /// 一、事件提示：把"被触碰的部位 + 动作"整理成发给 AI 的用户消息（<see cref="BuildEventPrompt"/>），
    ///     文案取自角色包 reactions.json 的 eventPrompt 段，缺省项用内置默认，触摸反应优先由 AI 现场生成回应；
    /// 二、备用台词：从角色包 reactions.json 的 reactions 段读取，仅在未接入 AI 或 AI 请求失败时使用
    ///     （台词只来自角色包：未提供该文件、或某个部位没写时，该角色/部位没有备用台词）。
    /// 左右与骨骼命名一致，指画面上的左右（Left 为画面左侧）。
    /// </summary>
    internal class TouchReactionService
    {
        /// <summary>反应台词配置文件名，位于角色包目录下。</summary>
        public const string ManifestFileName = "reactions.json";

        // ── 部位键（与 reactions.json 中的键一致）────

        public const string PartHead = "head";

        public const string PartArmLeft = "armLeft";

        public const string PartArmRight = "armRight";

        public const string PartLegLeft = "legLeft";

        public const string PartLegRight = "legRight";

        public const string PartBody = "body";

        // ── 事件提示键（与 reactions.json 里 eventPrompt 的键一致）────

        /// <summary>抚摸头部（按住头部来回滑动）的事件键。</summary>
        private const string EventHeadPet = "headPet";

        /// <summary>应答要求的事件键：整条提示共用的尾部指令。</summary>
        private const string EventInstructionKey = "instruction";

        /// <summary>部位键无法识别时使用的事件描述。</summary>
        private const string GenericEventAction = "主人碰了碰你";

        /// <summary>内置事件提示：角色包 eventPrompt 未提供或某项缺省时使用这一套。</summary>
        private static readonly Dictionary<string, string> BuiltInEventPrompt = new()
        {
            [EventHeadPet] = "主人按住你的头，轻轻来回抚摸了几下",
            [PartHead] = "主人戳了戳你的头",
            [PartArmLeft] = "主人戳了戳你的右手",
            [PartArmRight] = "主人戳了戳你的左手",
            [PartLegLeft] = "主人戳了戳你的右腿",
            [PartLegRight] = "主人戳了戳你的左腿",
            [PartBody] = "主人戳了戳你的身体",
            [EventInstructionKey] =
                "请用符合你人设的一句话回应，只输出这一句话（30 字以内），不要引号、不要旁白、不要解释。"
        };

        /// <summary>
        /// 内置兜底台词：节省模式下角色包未提供备用台词时使用，保证关闭 AI 后触摸仍有回应。
        /// </summary>
        private static readonly Dictionary<string, List<string>> BuiltInLines = new()
        {
            [PartHead] = new() { "唔……好舒服～", "嘿嘿，被你摸头了呢。" },
            [PartBody] = new() { "呀！别戳那里……", "嗯？找我有什么事吗？" },
            [PartArmLeft] = new() { "你碰了碰我的手。", "嗯？要牵手吗？" },
            [PartArmRight] = new() { "你碰了碰我的手。", "嗯？要牵手吗？" },
            [PartLegLeft] = new() { "别碰腿啦，好痒。", "唔……腿不是用来戳的啦。" },
            [PartLegRight] = new() { "别碰腿啦，好痒。", "唔……腿不是用来戳的啦。" }
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>各部位的备用台词表，由 Load 从角色包填充；未配置的部位没有备用台词。</summary>
        private readonly Dictionary<string, List<string>> _lines = new();

        /// <summary>各部位上一句说过的台词，用于避免连续两次触发说同一句。</summary>
        private readonly Dictionary<string, string> _lastLines = new();

        /// <summary>角色包提供的事件提示覆盖项（键同 eventPrompt），构建时优先于内置默认。</summary>
        private readonly Dictionary<string, string> _eventPromptOverrides = new();

        /// <summary>
        /// 从角色包目录加载 reactions.json：eventPrompt 段作为事件提示的覆盖项、reactions 段作为备用台词。
        /// 未提供该文件或解析失败时，事件提示全用内置默认，且没有备用台词。
        /// </summary>
        public void Load(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Info($"[触摸] 未找到 {ManifestFileName}，事件提示用内置默认，且该角色没有备用台词");
                return;
            }

            ReactionManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ReactionManifest>(
                    File.ReadAllText(manifestPath), JsonOptions);
            }
            catch (Exception ex)
            {
                Log.Warning($"[触摸] {ManifestFileName} 解析失败，事件提示用内置默认，且该角色没有备用台词：{ex.Message}");
                return;
            }

            if (manifest == null)
            {
                Log.Warning($"[触摸] {ManifestFileName} 内容为空，事件提示用内置默认，且该角色没有备用台词");
                return;
            }

            // 事件提示：只记录角色包的覆盖项，缺省项在构建时回退到内置默认
            var overridden = 0;
            if (manifest.EventPrompt != null)
            {
                foreach (var (key, text) in manifest.EventPrompt)
                {
                    var eventKey = ResolveEventKey(key);
                    if (eventKey == null)
                    {
                        Log.Warning($"[触摸] 跳过未知的事件提示键：{key}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    _eventPromptOverrides[eventKey] = text.Trim();
                    overridden++;
                }
            }

            // 备用台词
            var loaded = 0;
            foreach (var (key, lines) in manifest.Reactions)
            {
                var partKey = ResolveConfiguredKey(key);
                if (partKey == null)
                {
                    Log.Warning($"[触摸] 跳过未知的部位键：{key}");
                    continue;
                }

                var cleaned = lines?.Where(line => !string.IsNullOrWhiteSpace(line))
                                    .Select(line => line.Trim())
                                    .ToList();
                if (cleaned == null || cleaned.Count == 0) continue;

                _lines[partKey] = cleaned;
                _lastLines.Remove(partKey);
                loaded++;
            }

            Log.Info($"[触摸] 已加载 {loaded} 类备用台词、{overridden} 项事件提示覆盖：{Path.GetFileName(packageDir)}");
        }

        /// <summary>
        /// 随机取一条台词；同部位连续触发时避免重复上一句。该部位没有台词时返回 null。
        /// </summary>
        public string? PickLine(string partKey)
        {
            return _lines.TryGetValue(partKey, out var lines) ? PickFrom(lines, partKey) : null;
        }

        /// <summary>
        /// 取一条内置兜底台词：节省模式下角色包没有备用台词时使用，保证触摸仍有回应。
        /// 与备用台词共用“上一句”记录，避免连续重复。
        /// </summary>
        public string? PickBuiltInLine(string partKey)
        {
            return BuiltInLines.TryGetValue(partKey, out var lines) ? PickFrom(lines, partKey) : null;
        }

        /// <summary>从台词表里随机取一条，同部位避免与上一句重复；表为空时返回 null。</summary>
        private string? PickFrom(List<string> lines, string partKey)
        {
            if (lines.Count == 0)
                return null;

            var line = lines.Count == 1 ? lines[0] : lines[Random.Shared.Next(lines.Count)];

            // 与上一句相同则顺延到下一条，避免连续重复
            if (lines.Count > 1 && line == _lastLines.GetValueOrDefault(partKey))
            {
                line = lines[(lines.IndexOf(line) + 1) % lines.Count];
            }

            _lastLines[partKey] = line;
            return line;
        }

        /// <summary>
        /// 把骨骼 ID 映射为反应部位键；root 等不参与触摸反应的骨骼返回 null。
        /// </summary>
        public static string? ResolvePartKey(string? boneId)
        {
            if (string.IsNullOrEmpty(boneId)) return null;
            if (boneId == "head") return PartHead;
            if (boneId == "body") return PartBody;
            if (boneId.StartsWith("arm_left", StringComparison.Ordinal)) return PartArmLeft;
            if (boneId.StartsWith("arm_right", StringComparison.Ordinal)) return PartArmRight;
            if (boneId.StartsWith("leg_left", StringComparison.Ordinal)) return PartLegLeft;
            if (boneId.StartsWith("leg_right", StringComparison.Ordinal)) return PartLegRight;
            return null;
        }

        /// <summary>
        /// 构造触摸事件给 AI 的用户消息：说明被触碰的部位与动作，并要求一句符合人设的短回应。
        /// 文案优先取角色包 eventPrompt 的覆盖项，缺省项用内置默认；
        /// 部位按角色自身视角描述（画面左侧的手脚是角色的右手/右腿），与备用台词写法一致。
        /// </summary>
        /// <param name="partKey">反应部位键（head / armLeft / armRight / legLeft / legRight / body）。</param>
        /// <param name="isPetting">true 表示抚摸手势（按住头部来回滑动），false 表示双击。</param>
        public string BuildEventPrompt(string partKey, bool isPetting)
        {
            var eventKey = partKey == PartHead && isPetting ? EventHeadPet : partKey;
            var action = ResolveEventText(eventKey, GenericEventAction);
            var instruction = ResolveEventText(EventInstructionKey, string.Empty);

            return $"（触摸事件）{action}。{instruction}";
        }

        /// <summary>取事件提示文案：角色包覆盖项优先，其次内置默认，最后用调用方给的兜底文案。</summary>
        private string ResolveEventText(string eventKey, string fallback)
        {
            if (_eventPromptOverrides.TryGetValue(eventKey, out var text)) return text;
            if (BuiltInEventPrompt.TryGetValue(eventKey, out var builtIn)) return builtIn;
            return fallback;
        }

        /// <summary>把配置里的键统一到标准事件键（部位键 + headPet + instruction，忽略大小写与首尾空白）。</summary>
        private static string? ResolveEventKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            var partKey = ResolveConfiguredKey(key);
            if (partKey != null) return partKey;

            return key.Trim().ToLowerInvariant() switch
            {
                "headpet" => EventHeadPet,
                "instruction" => EventInstructionKey,
                _ => null
            };
        }

        /// <summary>把配置里的键统一到标准部位键（忽略大小写与首尾空白）。</summary>
        private static string? ResolveConfiguredKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            return key.Trim().ToLowerInvariant() switch
            {
                "head" => PartHead,
                "body" => PartBody,
                "armleft" => PartArmLeft,
                "armright" => PartArmRight,
                "legleft" => PartLegLeft,
                "legright" => PartLegRight,
                _ => null
            };
        }
    }
}