namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 一条长期记忆：内容、重要性、时间与访问统计，以及用于语义检索的向量。
    /// </summary>
    public class MemoryEntry
    {
        /// <summary>唯一标识。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>记忆正文。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>重要性，0~1，越高越优先保留与召回。</summary>
        public double Importance { get; set; } = 0.5;

        /// <summary>创建时间。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>最近一次被检索到的时间。</summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.Now;

        /// <summary>被检索到的次数。</summary>
        public int AccessCount { get; set; }

        /// <summary>文本向量，可能为空（未配置 Embedding 模型或尚未生成）。</summary>
        public float[]? Vector { get; set; }
    }
}
