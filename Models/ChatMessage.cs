namespace KfuPet.Models
{
    /// <summary>
    /// 一条对话消息，用于短期记忆（会话上下文）与工具调用的维护。
    /// </summary>
    public class ChatMessage
    {
        /// <summary>角色：system、user、assistant 或 tool。</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息内容；带工具调用的 assistant 消息可为 null。</summary>
        public string? Content { get; set; } = string.Empty;

        /// <summary>工具结果消息对应的 tool_call_id（仅 Role=tool 时使用）。</summary>
        public string? ToolCallId { get; set; }

        /// <summary>assistant 发起的工具调用列表（无工具调用时为 null）。</summary>
        public List<ToolCall>? ToolCalls { get; set; }
    }

    /// <summary>
    /// 一次工具调用：模型要求本地执行某个函数及其参数。
    /// </summary>
    public class ToolCall
    {
        /// <summary>本次调用的唯一标识，用于回填结果时对应。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>工具名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>工具参数（JSON 字符串）。</summary>
        public string Arguments { get; set; } = string.Empty;
    }
}
