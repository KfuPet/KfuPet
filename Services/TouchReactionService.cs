using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 角色反应文案服务：为触摸反应、拖动越界回正（snapBack）与主动搭话提供两类文案。
    /// 一、事件提示：把"发生了什么"整理成发给 AI 的用户消息（<see cref="BuildEventPrompt"/>、<see cref="BuildSnapBackEventPrompt"/>、<see cref="BuildProactiveEventPrompt"/>），
    ///     文案取自角色包 reactions.json 的 eventPrompt 段；
    /// 二、备用台词：从角色包 reactions.json 的 reactions 段读取，未接入 AI 或 AI 请求失败时使用。
    /// 两类文案均完全由角色包提供，代码不再内置默认；启动时 <see cref="Load"/> 会检测配置完整性并记录日志。
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

        // ── 事件键（与 reactions.json 中 eventPrompt / reactions 的键一致）────

        /// <summary>拖动越界回正（差点被拖出屏幕）的事件键：eventPrompt 与 reactions 两段通用。</summary>
        public const string EventSnapBack = "snapBack";

        /// <summary>独处搭话的事件键：在电脑前但有一阵子没和桌宠互动。</summary>
        public const string EventIdleChat = "idleChat";

        /// <summary>欢迎回来的事件键：离开较久后刚回到电脑前。</summary>
        public const string EventWelcomeBack = "welcomeBack";

        /// <summary>早上问候的事件键。</summary>
        public const string EventMorningGreeting = "morningGreeting";

        /// <summary>晚上问候的事件键。</summary>
        public const string EventEveningGreeting = "eveningGreeting";

        // ── 事件提示键（与 reactions.json 里 eventPrompt 的键一致）────

        /// <summary>抚摸头部（按住头部来回滑动）的事件键。</summary>
        private const string EventHeadPet = "headPet";

        /// <summary>应答要求的事件键：整条提示共用的尾部指令。</summary>
        private const string EventInstructionKey = "instruction";

        // ── 完整性检测：reactions.json 必须提供的键（代码不再内置默认文案）────

        /// <summary>eventPrompt 段必须提供的键。</summary>
        private static readonly string[] RequiredEventPromptKeys =
        {
            EventHeadPet, PartHead, PartArmLeft, PartArmRight, PartLegLeft, PartLegRight, PartBody,
            EventSnapBack, EventIdleChat, EventWelcomeBack, EventMorningGreeting, EventEveningGreeting,
            EventInstructionKey
        };

        /// <summary>reactions 段必须提供的键（每个键至少一条非空台词）。</summary>
        private static readonly string[] RequiredReactionKeys =
        {
            PartHead, PartArmLeft, PartArmRight, PartLegLeft, PartLegRight, PartBody, EventSnapBack,
            EventIdleChat, EventWelcomeBack, EventMorningGreeting, EventEveningGreeting
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

        /// <summary>角色包提供的事件提示（键同 eventPrompt），构建触摸/拖动事件消息时取用。</summary>
        private readonly Dictionary<string, string> _eventPromptOverrides = new();

        /// <summary>最近一次加载检测出的缺失必填键；为空表示配置完整或尚未检测。</summary>
        private IReadOnlyList<string> _missingConfigKeys = Array.Empty<string>();

        /// <summary>最近一次加载检测出的缺失必填键（eventPrompt.* / reactions.*）；为空表示配置完整。</summary>
        public IReadOnlyList<string> MissingConfigKeys => _missingConfigKeys;

        /// <summary>
        /// 从角色包目录加载 reactions.json：eventPrompt 段作为事件提示、reactions 段作为备用台词。
        /// 加载完成后检测配置完整性，缺失的必填键会记录日志警告。
        /// </summary>
        public void Load(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Warning($"[触摸] 未找到 {ManifestFileName}：{Path.GetFileName(packageDir)}");
                CheckCompleteness(packageDir);
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
                Log.Warning($"[触摸] {ManifestFileName} 解析失败：{ex.Message}");
                CheckCompleteness(packageDir);
                return;
            }

            if (manifest == null)
            {
                Log.Warning($"[触摸] {ManifestFileName} 内容为空");
                CheckCompleteness(packageDir);
                return;
            }

            // 事件提示：仅记录角色包提供的项
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

            Log.Info($"[触摸] 已加载 {loaded} 类备用台词、{overridden} 项事件提示：{Path.GetFileName(packageDir)}");
            CheckCompleteness(packageDir);
        }

        /// <summary>
        /// 检测当前角色包反应配置是否完整：列出缺失的必填键并记录到 <see cref="MissingConfigKeys"/>，
        /// 缺失时记录日志警告，完整时记录 Info。必填键见 <see cref="RequiredEventPromptKeys"/>、<see cref="RequiredReactionKeys"/>。
        /// </summary>
        private void CheckCompleteness(string packageDir)
        {
            var missing = new List<string>();
            foreach (var key in RequiredEventPromptKeys)
            {
                if (!_eventPromptOverrides.ContainsKey(key))
                {
                    missing.Add($"eventPrompt.{key}");
                }
            }

            foreach (var key in RequiredReactionKeys)
            {
                if (!_lines.TryGetValue(key, out var lines) || lines.Count == 0)
                {
                    missing.Add($"reactions.{key}");
                }
            }

            _missingConfigKeys = missing;

            var name = Path.GetFileName(packageDir);
            if (missing.Count == 0)
            {
                Log.Info($"[触摸] 角色包反应配置完整：{name}");
                return;
            }

            Log.Warning($"[触摸] 角色包反应配置不完整，缺少 {missing.Count} 项：{string.Join("、", missing)}" +
                        $"（{name}，补全方法见 docs/角色包制作指南.md）");
        }

        /// <summary>
        /// 随机取一条台词；同部位连续触发时避免重复上一句。该部位没有台词时返回 null。
        /// </summary>
        public string? PickLine(string partKey)
        {
            return _lines.TryGetValue(partKey, out var lines) ? PickFrom(lines, partKey) : null;
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
        /// 文案取自角色包 eventPrompt；未提供该事件描述时返回空串（调用方应跳过 AI 请求）。
        /// 部位按角色自身视角描述（画面左侧的手脚是角色的右手/右腿），与备用台词写法一致。
        /// </summary>
        /// <param name="partKey">反应部位键（head / armLeft / armRight / legLeft / legRight / body）。</param>
        /// <param name="isPetting">true 表示抚摸手势（按住头部来回滑动），false 表示双击。</param>
        public string BuildEventPrompt(string partKey, bool isPetting)
        {
            var eventKey = partKey == PartHead && isPetting ? EventHeadPet : partKey;
            return BuildEventMessage("触摸事件", eventKey);
        }

        /// <summary>
        /// 构造拖动越界事件给 AI 的用户消息：说明主人拖动时差点把你拖出屏幕、你已及时靠边站稳，
        /// 并要求一句符合人设的短回应。文案取自角色包 eventPrompt 的 snapBack；未提供时返回空串。
        /// </summary>
        public string BuildSnapBackEventPrompt()
        {
            return BuildEventMessage("拖动事件", EventSnapBack);
        }

        /// <summary>
        /// 构造主动搭话事件给 AI 的用户消息：把动态语境（当前时间、间隔时长）与角色包的场景描述拼成一段，
        /// 并要求一句符合人设的短回应。文案取自角色包 eventPrompt；未提供该事件描述时返回空串（调用方应跳过 AI 请求）。
        /// </summary>
        /// <param name="eventKey">主动搭话事件键（idleChat / welcomeBack / morningGreeting / eveningGreeting）。</param>
        /// <param name="contextNote">动态语境（当前时间、间隔时长等）；为空时不带语境。</param>
        public string BuildProactiveEventPrompt(string eventKey, string contextNote)
        {
            var action = ResolveEventText(eventKey);
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            var instruction = ResolveEventText(EventInstructionKey);
            var text = string.IsNullOrWhiteSpace(instruction) ? action : $"{action}。{instruction}";
            var context = string.IsNullOrWhiteSpace(contextNote) ? string.Empty : contextNote;
            return $"（主动搭话）{context}{text}";
        }

        /// <summary>拼接事件给 AI 的用户消息；角色包未提供该事件描述时返回空串。</summary>
        private string BuildEventMessage(string eventLabel, string eventKey)
        {
            var action = ResolveEventText(eventKey);
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            var instruction = ResolveEventText(EventInstructionKey);
            var text = string.IsNullOrWhiteSpace(instruction) ? action : $"{action}。{instruction}";
            return $"（{eventLabel}）{text}";
        }

        /// <summary>取事件提示文案：仅在角色包提供时返回，未提供返回 null。</summary>
        private string? ResolveEventText(string eventKey)
        {
            return _eventPromptOverrides.TryGetValue(eventKey, out var text) ? text : null;
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

        /// <summary>把配置里的键统一到标准部位键或事件键（忽略大小写与首尾空白）。</summary>
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
                "snapback" => EventSnapBack,
                "idlechat" => EventIdleChat,
                "welcomeback" => EventWelcomeBack,
                "morninggreeting" => EventMorningGreeting,
                "eveninggreeting" => EventEveningGreeting,
                _ => null
            };
        }
    }
}