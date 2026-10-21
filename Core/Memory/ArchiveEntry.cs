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

        /// <summary>该轮对话是否已由实时通道提炼并写入长期记忆；为 true 时归档分析直接丢弃，不再重复提炼。
        /// 实时通道没有写入任何信息的对话保持 false，继续等待归档统一处理。</summary>
        public bool RealtimeExtracted { get; set; }
    }
}
