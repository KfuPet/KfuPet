using KfuPet.Core.Memory;

namespace KfuPet.Services
{
    /// <summary>
    /// 长期记忆管理器：负责记忆的写入、关键词检索与注入上下文的构建。
    /// </summary>
    internal class MemoryManager
    {
        private readonly MemoryStore _store = new();
        private readonly StopWordsService _stopWordsService;
        private readonly List<MemoryEntry> _entries;
        private readonly object _lock = new();

        public MemoryManager(StopWordsService stopWordsService)
        {
            _stopWordsService = stopWordsService;
            _entries = _store.Load();
        }

        /// <summary>当前记忆总数。</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// 本地关键词检索：从查询提取关键词，返回内容包含这些关键词的记忆，零 API 开销。
        /// </summary>
        public IReadOnlyList<string> KeywordSearch(string query, int topK)
        {
            var keywords = ExtractKeywords(query);
            if (keywords.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<MemoryEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<MemoryEntry>(_entries);
            }

            return snapshot
                .Where(e => keywords.Any(k => e.Content.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => keywords.Count(k => e.Content.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(e => e.Importance)
                .Take(topK)
                .Select(e => e.Content)
                .ToList();
        }

        /// <summary>从查询文本提取关键词：去标点、去停用词，按空白切分后取长度 ≥ 2 的片段。</summary>
        private List<string> ExtractKeywords(string text)
        {
            // 标点/空白 → 空格
            var sb = new System.Text.StringBuilder();
            foreach (var c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            var normalized = sb.ToString();

            // 停用词 → 空格（作为切分点）
            foreach (var w in _stopWordsService.Words)
            {
                normalized = normalized.Replace(w, " ");
            }

            return normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 写入一条新记忆：去重后落盘。
        /// </summary>
        public Task StoreAsync(string content, int importance)
        {
            content = content.Trim();
            if (string.IsNullOrEmpty(content))
            {
                return Task.CompletedTask;
            }

            // 去重：内容完全相同的已有记忆直接跳过，避免重复入库
            lock (_lock)
            {
                if (_entries.Any(e => string.Equals(e.Content.Trim(), content, StringComparison.OrdinalIgnoreCase)))
                {
                    return Task.CompletedTask;
                }
            }

            var entry = new MemoryEntry
            {
                Content = content,
                Importance = Math.Clamp(importance, 0, 5)
            };

            lock (_lock)
            {
                _entries.Add(entry);
                _store.Save(_entries);
            }

            return Task.CompletedTask;
        }

        /// <summary>获取全部长期记忆的快照。</summary>
        public IReadOnlyList<MemoryEntry> Snapshot()
        {
            lock (_lock)
            {
                return new List<MemoryEntry>(_entries);
            }
        }

        /// <summary>按 id 删除若干条长期记忆并落盘。</summary>
        public void RemoveByIds(IReadOnlyCollection<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                _entries.RemoveAll(e => ids.Contains(e.Id));
                _store.Save(_entries);
            }
        }

        /// <summary>
        /// 去重：内容完全相同（忽略首尾空白与大小写）的记忆只保留重要度最高的一条，返回被删除的数量。
        /// </summary>
        public int Deduplicate()
        {
            var removed = 0;
            lock (_lock)
            {
                var result = new List<MemoryEntry>();
                foreach (var entry in _entries)
                {
                    var existing = result.FirstOrDefault(e =>
                        string.Equals(e.Content.Trim(), entry.Content.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        result.Add(entry);
                        continue;
                    }

                    // 与已保留的某条重复：保留重要度更高者
                    if (entry.Importance > existing.Importance)
                    {
                        result.Remove(existing);
                        result.Add(entry);
                    }
                    removed++;
                }

                if (removed > 0)
                {
                    _entries.Clear();
                    _entries.AddRange(result);
                    _store.Save(_entries);
                }
            }

            return removed;
        }

        /// <summary>把记忆列表拼成注入 system prompt 的文本。</summary>
        public static string BuildContext(IReadOnlyList<string> memories)
        {
            if (memories.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("\n", memories.Select(m => "- " + m));
        }
    }
}
