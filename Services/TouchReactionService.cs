using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 触摸反应服务：从角色包读取 reactions.json（各部位被触摸时的台词），
    /// 文件缺失或解析失败时退回内置默认台词，保证任何角色都能响应触摸。
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

        /// <summary>各部位的台词表：构造时填入内置默认台词，加载角色包后按部位覆盖。</summary>
        private readonly Dictionary<string, List<string>> _lines = new();

        /// <summary>各部位上一句说过的台词，用于避免连续两次触发说同一句。</summary>
        private readonly Dictionary<string, string> _lastLines = new();

        public TouchReactionService()
        {
            ApplyDefaultLines();
        }

        /// <summary>
        /// 从角色包目录加载 reactions.json；未提供该文件或解析失败时保留内置默认台词。
        /// </summary>
        public void Load(string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Info($"[触摸] 未找到 {ManifestFileName}，使用内置默认台词");
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
                Log.Warning($"[触摸] {ManifestFileName} 解析失败，使用内置默认台词：{ex.Message}");
                return;
            }

            if (manifest?.Reactions == null || manifest.Reactions.Count == 0)
            {
                Log.Warning($"[触摸] {ManifestFileName} 中没有台词定义，使用内置默认台词");
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

        /// <summary>
        /// 内置默认台词：角色包没有提供 reactions.json 时使用，保证触摸反应始终可用。
        /// </summary>
        private void ApplyDefaultLines()
        {
            _lines[PartHead] = new List<string>
            {
                "唔……摸头要提前打招呼的啦～",
                "嘿嘿，好舒服……再摸一下？",
                "头发要被揉乱啦！"
            };

            // 台词正文按角色自身视角写：画面左侧的手脚是角色的右手/右腿
            _lines[PartArmLeft] = new List<string>
            {
                "呀！右手被捏到了～",
                "哼，再捏我就要还手了哦！",
                "这只手要留着干正事的啦！"
            };

            _lines[PartArmRight] = new List<string>
            {
                "呀！左手被抓住了～",
                "喂喂，这只手还要干正事的！",
                "轻一点啦，别拽～"
            };

            _lines[PartLegLeft] = new List<string>
            {
                "呀，右腿痒痒的～",
                "再戳我就不站着了，我要坐下！",
                "右腿也是要好好保护的呀～"
            };

            _lines[PartLegRight] = new List<string>
            {
                "痒痒的！左腿不许戳～",
                "别戳啦，站不稳了～",
                "左腿可是站得最稳的那条！"
            };

            _lines[PartBody] = new List<string>
            {
                "喂喂，戳肚子会痒的啦！",
                "呀！偷袭是不讲武德的！",
                "哼哼，我才不怕痒呢……才怪～"
            };
        }
    }
}