using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 长期记忆的持久化存储：记忆正文与元数据写入 <c>memories.md</c>（人类可读、可解析），
    /// 向量单独缓存到 <c>vectors.json</c>（丢失可重建）。
    /// 记忆统一存放在 %AppData%\KfuPet\Memory。
    /// </summary>
    internal class MemoryStore
    {
        private readonly string _mdPath;
        private readonly string _vectorPath;

        public MemoryStore()
        {
            var directory = ResolveMemoryDirectory();
            _mdPath = Path.Combine(directory, "memories.md");
            _vectorPath = Path.Combine(directory, "vectors.json");
        }

        /// <summary>从磁盘加载全部记忆（md 提供内容与元数据，json 补充向量）。</summary>
        public List<MemoryEntry> Load()
        {
            var entries = LoadFromMarkdown();
            AttachVectors(entries);
            return entries;
        }

        /// <summary>把全部记忆写回磁盘（md + 向量缓存）。</summary>
        public void Save(IReadOnlyList<MemoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_mdPath)!);
                File.WriteAllText(_mdPath, BuildMarkdown(entries));

                var vectors = new Dictionary<string, float[]>();
                foreach (var entry in entries)
                {
                    if (entry.Vector != null)
                    {
                        vectors[entry.Id] = entry.Vector;
                    }
                }
                File.WriteAllText(_vectorPath, JsonSerializer.Serialize(vectors));
            }
            catch
            {
                // 写入失败不阻断对话主流程
            }
        }

        /// <summary>解析 memories.md，提取记忆正文与元数据。</summary>
        private List<MemoryEntry> LoadFromMarkdown()
        {
            var entries = new List<MemoryEntry>();
            if (!File.Exists(_mdPath))
            {
                return entries;
            }

            var lines = File.ReadAllLines(_mdPath);
            MemoryEntry? current = null;
            var inMeta = false;
            var content = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                if (line.StartsWith("---", StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        current.Content = content.ToString().Trim();
                        entries.Add(current);
                        content.Clear();
                    }

                    current = new MemoryEntry();
                    inMeta = true;
                    continue;
                }

                if (current == null)
                {
                    continue; // 标题或分隔符前的空行
                }

                if (inMeta)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        inMeta = false;
                        continue;
                    }

                    var colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        ApplyMeta(current, line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
                        continue;
                    }

                    inMeta = false;
                }

                content.AppendLine(line);
            }

            if (current != null)
            {
                current.Content = content.ToString().Trim();
                entries.Add(current);
            }

            return entries;
        }

        private static void ApplyMeta(MemoryEntry entry, string key, string value)
        {
            switch (key)
            {
                case "id":
                    entry.Id = value;
                    break;
                case "importance":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var importance))
                    {
                        entry.Importance = importance;
                    }
                    break;
                case "created":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                    {
                        entry.CreatedAt = created;
                    }
                    break;
                case "accessed":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var accessed))
                    {
                        entry.LastAccessedAt = accessed;
                    }
                    break;
                case "count":
                    if (int.TryParse(value, out var count))
                    {
                        entry.AccessCount = count;
                    }
                    break;
            }
        }

        private void AttachVectors(List<MemoryEntry> entries)
        {
            if (!File.Exists(_vectorPath))
            {
                return;
            }

            try
            {
                var vectors = JsonSerializer.Deserialize<Dictionary<string, float[]>>(
                    File.ReadAllText(_vectorPath));
                if (vectors == null)
                {
                    return;
                }

                foreach (var entry in entries)
                {
                    if (vectors.TryGetValue(entry.Id, out var vector))
                    {
                        entry.Vector = vector;
                    }
                }
            }
            catch
            {
                // 向量缓存损坏时忽略，检索时回退关键词
            }
        }

        private static string BuildMarkdown(IReadOnlyList<MemoryEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# KfuPet 长期记忆");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                sb.AppendLine("---");
                sb.AppendLine($"id: {entry.Id}");
                sb.AppendLine($"importance: {entry.Importance.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"created: {entry.CreatedAt:o}");
                sb.AppendLine($"accessed: {entry.LastAccessedAt:o}");
                sb.AppendLine($"count: {entry.AccessCount}");
                sb.AppendLine("---");
                sb.AppendLine(entry.Content);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 记忆目录统一存放在用户数据目录 %AppData%\KfuPet\Memory。
        /// </summary>
        private static string ResolveMemoryDirectory()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Memory");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
