namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 一条短期记忆：一轮对话（用户 + 助手），保存原始消息，不参与重要性评分。
    /// </summary>
    public class ShortMemoryEntry
    {
        /// <summary>对话发生时间。</summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>用户消息。</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>助手回复。</summary>
        public string Assistant { get; set; } = string.Empty;
    }
}
