using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 触摸反应服务：从角色包读取 reactions.json（各部位被触摸时的台词）。
    /// 台词只来自角色包：未提供该文件、或某个部位没写时，该角色/部位触摸不会有反应。
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

        /// <summary>各部位的台词表，由 Load 从角色包填充；未配置的部位没有台词。</summary>
        private readonly Dictionary<string, List<string>> _lines = new();

        /// <summary>各部位上一句说过的台词，用于避免连续两次触发说同一句。</summary>
        private readonly Dictionary<string, string> _lastLines = new();

        /// <summary>
        /// 从角色包目录加载 reactions.json；未提供该文件或解析失败时没有任何台词可用。
        /// </summary>
        public void Load(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Info($"[触摸] 未找到 {ManifestFileName}，该角色触摸时不会说话");
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
                Log.Warning($"[触摸] {ManifestFileName} 解析失败，该角色触摸时不会说话：{ex.Message}");
                return;
            }

            if (manifest?.Reactions == null || manifest.Reactions.Count == 0)
            {
                Log.Warning($"[触摸] {ManifestFileName} 中没有台词定义，该角色触摸时不会说话");
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