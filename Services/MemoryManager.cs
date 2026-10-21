using KfuPet.Core.Memory;

namespace KfuPet.Services
{
    /// <summary>
    /// 长期记忆管理器：负责记忆的写入、关键词检索与注入上下文的构建。
    /// </summary>
    internal class MemoryManager
    {
        /// <summary>近重复判定的字符二元组相似度阈值（Dice 系数），越高越严格。</summary>
        private const double SimilarityThreshold = 0.8;

        /// <summary>参与近重复比较的最短归一化长度，过短的记忆不做模糊合并，避免误判。</summary>
        private const int MinSimilarityLength = 8;

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
        /// 本地检索：先从查询提取关键词精确匹配，再补一层字符级二元组（bigram）模糊匹配，
        /// 尽量在本地命中记忆、减少跌落到模型判断的次数，零 API 开销。
        /// </summary>
        public IReadOnlyList<string> KeywordSearch(string query, int topK)
        {
            var keywords = ExtractKeywords(query);
            var bigrams = ExtractBigrams(query);
            if (keywords.Count == 0 && bigrams.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<MemoryEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<MemoryEntry>(_entries);
            }

            return snapshot
                .Select(e => new
                {
                    Entry = e,
                    KeywordHits = keywords.Count(k => e.Content.Contains(k, StringComparison.OrdinalIgnoreCase)),
                    BigramHits = bigrams.Count(b => e.Content.Contains(b, StringComparison.OrdinalIgnoreCase))
                })
                // 关键词精确命中，或命中 ≥2 个二元组（过滤单个常见片段带来的噪声）
                .Where(x => x.KeywordHits > 0 || x.BigramHits >= 2)
                .OrderByDescending(x => x.KeywordHits * 2 + x.BigramHits)
                .ThenByDescending(x => x.Entry.Importance)
                .Take(topK)
                .Select(x => x.Entry.Content)
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

            // 停用词 → 空格（作为切分点）；按词长降序替换，避免「你」先切断「你们」这类长词
            foreach (var w in _stopWordsService.Words.OrderByDescending(w => w.Length))
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
        /// 提取字符级二元组（bigram）：中文无空格、词边界难以还原，
        /// 用相邻两字片段作为模糊匹配单元，弥补关键词切分的漏召；仅对含中文的片段生成，避免英文噪声。
        /// </summary>
        private static List<string> ExtractBigrams(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            var result = new List<string>();
            foreach (var run in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!run.Any(IsCjk))
                {
                    continue;
                }

                for (var i = 0; i + 1 < run.Length; i++)
                {
                    result.Add(run.Substring(i, 2));
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>是否为基本区中日韩统一表意文字（常用于识别中文片段）。</summary>
        private static bool IsCjk(char c) => c >= '\u4E00' && c <= '\u9FFF';

        /// <summary>
        /// 写入一条新记忆：归一化精确去重 + 近重复合并后落盘。
        /// 同一事实换一种措辞（如「用户的生日是3月6日」与「用户生日是3月6日。」）不会重复入库。
        /// </summary>
        public Task StoreAsync(string content, int importance)
        {
            content = content.Trim();
            if (string.IsNullOrEmpty(content))
            {
                return Task.CompletedTask;
            }

            var normalized = NormalizeForCompare(content);

            lock (_lock)
            {
                foreach (var existing in _entries)
                {
                    var existingNormalized = NormalizeForCompare(existing.Content);
                    if (!IsSameFact(normalized, existingNormalized))
                    {
                        continue;
                    }

                    // 同一事实只保留一条：新内容更完整时替换旧内容，重要性取较高者
                    if (normalized.Length > existingNormalized.Length)
                    {
                        existing.Content = content;
                        existing.Importance = Math.Max(existing.Importance, Math.Clamp(importance, 0, 5));
                        _store.Save(_entries);
                        Log.Debug($"[记忆] 近重复记忆已合并（保留更完整的版本）：{content}");
                    }
                    else
                    {
                        Log.Debug($"[记忆] 跳过重复记忆：{content}");
                    }

                    return Task.CompletedTask;
                }

                var entry = new MemoryEntry
                {
                    Content = content,
                    Importance = Math.Clamp(importance, 0, 5)
                };
                _entries.Add(entry);
                _store.Save(_entries);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 判断两条归一化后的记忆是否为同一事实：完全相同、一方包含另一方，
        /// 或字符二元组相似度达到阈值（允许「的」这类虚词差异）。
        /// </summary>
        private static bool IsSameFact(string normalized, string existingNormalized)
        {
            if (string.Equals(normalized, existingNormalized, StringComparison.Ordinal))
            {
                return true;
            }

            if (Math.Min(normalized.Length, existingNormalized.Length) < MinSimilarityLength)
            {
                return false;
            }

            if (normalized.Contains(existingNormalized, StringComparison.Ordinal) ||
                existingNormalized.Contains(normalized, StringComparison.Ordinal))
            {
                return true;
            }

            return BigramSimilarity(normalized, existingNormalized) >= SimilarityThreshold;
        }

        /// <summary>字符二元组 Dice 相似度：2×交集 / (A 组数 + B 组数)，取值 0~1。</summary>
        private static double BigramSimilarity(string a, string b)
        {
            var setA = ExtractBigrams(a);
            var setB = ExtractBigrams(b);
            if (setA.Count == 0 || setB.Count == 0)
            {
                return 0;
            }

            var intersection = setA.Count(x => setB.Contains(x));
            return 2.0 * intersection / (setA.Count + setB.Count);
        }

        /// <summary>比较前归一化：去掉空白、标点与符号并统一小写，消除措辞中的格式差异。</summary>
        private static string NormalizeForCompare(string content)
        {
            var sb = new System.Text.StringBuilder(content.Length);
            foreach (var c in content)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    continue;
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        /// <summary>从磁盘重新加载长期记忆，覆盖当前内存中的缓存。</summary>
        public void Reload()
        {
            lock (_lock)
            {
                _entries.Clear();
                _entries.AddRange(_store.Load());
            }
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

        /// <summary>清空全部长期记忆并落盘。</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
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
