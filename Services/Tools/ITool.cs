namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 一个可供 AI 模型调用的本地工具。模型通过 Function Calling 返回工具名与参数，
    /// 由 <see cref="ExecuteAsync"/> 本地执行后把结果回填给模型。
    /// </summary>
    public interface ITool
    {
        /// <summary>工具名称，作为 Function Calling 的 function.name。</summary>
        string Name { get; }

        /// <summary>工具用途描述，帮助模型判断何时调用。</summary>
        string Description { get; }

        /// <summary>参数 JSON Schema（OpenAI 兼容的 parameters 对象）。</summary>
        string ParametersSchemaJson { get; }

        /// <summary>执行工具并返回结果文本。</summary>
        /// <param name="argumentsJson">模型传入的参数（JSON 字符串）。</param>
        Task<string> ExecuteAsync(string argumentsJson);
    }
}
