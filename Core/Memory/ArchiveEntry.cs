namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 一条归档记忆：短期与长期之间的缓冲，可能是原始对话（conversation），
    /// 也可能是分析后未入选信息的浓缩（summary）。
    /// </summary>
    public class ArchiveEntry
    {
        /// <summary>记录类型：conversation（原始对话）或 summary（总结条目）。</summary>
        public string Type { get; set; } = "conversation";

        /// <summary>对话发生时间（conversation 类型使用）。</summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>用户消息（conversation 类型使用）。</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>助手回复（conversation 类型使用）。</summary>
        public string Assistant { get; set; } = string.Empty;

        /// <summary>总结正文（summary 类型使用）。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>重要性，0~5 整数（summary 类型使用）。</summary>
        public int Importance { get; set; }

        /// <summary>是否为总结条目。</summary>
        public bool IsSummary { get; set; }
    }
}
