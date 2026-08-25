namespace KfuPet.Models
{
    /// <summary>
    /// 一条对话消息，用于短期记忆（会话上下文）的维护。
    /// </summary>
    public class ChatMessage
    {
        /// <summary>角色：user 或 assistant。</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息内容。</summary>
        public string Content { get; set; } = string.Empty;
    }
}
