using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 触摸反应服务：汇总触摸事件所需的两类文案。
    /// 一、事件提示：把"被触碰的部位 + 动作"整理成发给 AI 的用户消息（<see cref="BuildEventPrompt"/>），
    ///     触摸反应优先由 AI 现场生成回应；
    /// 二、备用台词：从角色包 reactions.json 读取，仅在未接入 AI 或 AI 请求失败时使用
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

        /// <summary>
        /// 从角色包目录加载 reactions.json；未提供该文件或解析失败时没有备用台词可用。
        /// </summary>
        public void Load(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Info($"[触摸] 未找到 {ManifestFileName}，该角色没有备用台词");
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
                Log.Warning($"[触摸] {ManifestFileName} 解析失败，该角色没有备用台词：{ex.Message}");
                return;
            }

            if (manifest?.Reactions == null || manifest.Reactions.Count == 0)
            {
                Log.Warning($"[触摸] {ManifestFileName} 中没有台词定义，该角色没有备用台词");
                return;
            }

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

            Log.Info($"[触摸] 已加载 {loaded} 类反应台词：{Path.GetFileName(packageDir)}");
        }

        /// <summary>
        /// 随机取一条台词；同部位连续触发时避免重复上一句。该部位没有台词时返回 null。
        /// </summary>
        public string? PickLine(string partKey)
        {
            if (!_lines.TryGetValue(partKey, out var lines) || lines.Count == 0)
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
        /// 部位按角色自身视角描述（画面左侧的手脚是角色的右手/右腿），与 reactions.json 的写法一致。
        /// </summary>
        /// <param name="partKey">反应部位键（head / armLeft / armRight / legLeft / legRight / body）。</param>
        /// <param name="isPetting">true 表示抚摸手势（按住头部来回滑动），false 表示双击。</param>
        public static string BuildEventPrompt(string partKey, bool isPetting)
        {
            var action = partKey switch
            {
                PartHead when isPetting => "主人按住你的头，轻轻来回抚摸了几下",
                PartHead => "主人戳了戳你的头",
                PartArmLeft => "主人戳了戳你的右手",
                PartArmRight => "主人戳了戳你的左手",
                PartLegLeft => "主人戳了戳你的右腿",
                PartLegRight => "主人戳了戳你的左腿",
                PartBody => "主人戳了戳你的身体",
                _ => "主人碰了碰你"
            };

            return $"（触摸事件）{action}。请用符合你人设的一句话回应，" +
                   "只输出这一句话（30 字以内），不要引号、不要旁白、不要解释。";
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