using KfuPet.Core.Memory;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 长期记忆管理器：负责记忆的语义检索、写入与注入上下文的构建。
    /// 检索优先使用向量相似度（需配置 Embedding 模型），未配置时回退为按重要性排序。
    /// </summary>
    internal class MemoryManager
    {
        private readonly MemoryStore _store = new();
        private readonly EmbeddingService _embeddingService = new();
        private readonly List<MemoryEntry> _entries;
        private readonly object _lock = new();

        public MemoryManager()
        {
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
        /// 检索与查询最相关的记忆文本，最多返回 <paramref name="topK"/> 条，
        /// 并更新被命中记忆的访问统计。
        /// </summary>
        public async Task<IReadOnlyList<string>> SearchAsync(ModelConfig model, string query, int topK)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<string>();
            }

            List<MemoryEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<MemoryEntry>(_entries);
            }

            if (snapshot.Count == 0)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<MemoryEntry> ranked = await RankAsync(model, query, snapshot);

            var result = new List<string>();
            lock (_lock)
            {
                foreach (var entry in ranked.Take(topK))
                {
                    entry.LastAccessedAt = DateTime.Now;
                    entry.AccessCount++;
                    result.Add(entry.Content);
                }

                if (ranked.Count > 0)
                {
                    _store.Save(_entries);
                }
            }

            return result;
        }

        /// <summary>
        /// 写入一条新记忆：先为内容生成向量（若配置了 Embedding 模型），再落盘。
        /// </summary>
        public async Task StoreAsync(ModelConfig model, string content, double importance)
        {
            content = content.Trim();
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            float[]? vector = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(model.EmbeddingModelId))
                {
                    vector = await _embeddingService.EmbedAsync(model, content);
                }
            }
            catch
            {
                // 向量生成失败不阻断记忆写入，之后可回退关键词排序
            }

            var entry = new MemoryEntry
            {
                Content = content,
                Importance = Math.Clamp(importance, 0, 1),
                Vector = vector
            };

            lock (_lock)
            {
                _entries.Add(entry);
                _store.Save(_entries);
            }
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

        /// <summary>
        /// 对记忆排序：优先向量余弦相似度，其次按重要性 × 访问热度排序。
        /// </summary>
        private async Task<IReadOnlyList<MemoryEntry>> RankAsync(ModelConfig model, string query, IReadOnlyList<MemoryEntry> entries)
        {
            float[]? queryVector = null;
            if (!string.IsNullOrWhiteSpace(model.EmbeddingModelId))
            {
                try
                {
                    queryVector = await _embeddingService.EmbedAsync(model, query);
                }
                catch
                {
                    queryVector = null;
                }
            }

            if (queryVector != null)
            {
                return entries
                    .Select(e => new { Entry = e, Score = e.Vector == null ? 0.0 : CosineSimilarity(queryVector, e.Vector) })
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Entry)
                    .ToList();
            }

            // 无向量时按重要性 + 访问热度排序，保证仍能召回较重要的记忆
            return entries
                .OrderByDescending(e => e.Importance * 10 + Math.Min(e.AccessCount, 5))
                .ToList();
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
            {
                return 0;
            }

            double dot = 0, normA = 0, normB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
            {
                return 0;
            }

            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }
    }
}
