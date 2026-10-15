namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 工具定义的轻量描述，供 ChatService 序列化到 OpenAI 兼容的 tools 字段。
    /// </summary>
    public sealed class ToolDefinition
    {
        /// <summary>工具名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>工具用途描述。</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>参数 JSON Schema（字符串）。</summary>
        public string ParametersJson { get; set; } = "{}";
    }

    /// <summary>
    /// 工具注册表：持有全部已注册工具，负责生成工具定义列表并按名称分发执行。
    /// </summary>
    internal class ToolRegistry
    {
        private readonly List<ITool> _tools = new();

        /// <summary>注册一个工具。</summary>
        public void Register(ITool tool)
        {
            _tools.Add(tool);
        }

        /// <summary>
        /// 生成工具定义列表，供请求携带；传入 <paramref name="filter"/> 时只包含通过筛选的工具
        /// （用于按用户设置对模型隐藏某些工具）。
        /// </summary>
        public IReadOnlyList<ToolDefinition> GetDefinitions(Func<ITool, bool>? filter = null)
        {
            return _tools
                .Where(t => filter?.Invoke(t) ?? true)
                .Select(t => new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    ParametersJson = t.ParametersSchemaJson
                })
                .ToList();
        }

        /// <summary>按名称执行工具，找不到时返回提示文本。</summary>
        public async Task<string> ExecuteAsync(string name, string argumentsJson)
        {
            var tool = _tools.FirstOrDefault(t => t.Name == name);
            if (tool == null)
            {
                return $"未知工具：{name}";
            }

            return await tool.ExecuteAsync(argumentsJson);
        }
    }
}
