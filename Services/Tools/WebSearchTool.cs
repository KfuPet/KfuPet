namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 联网搜索工具（占位实现）：当前搜索后端未接入，先返回固定提示，
    /// 让模型据此诚实地告知用户"搜索功能暂未配置"，避免编造实时信息。
    /// 后续接入真实后端（如 Tavily / Bing）时，仅替换本类的 ExecuteAsync 内部逻辑。
    /// </summary>
    internal class WebSearchTool : ITool
    {
        public string Name => "web_search";

        public string Description => "联网搜索最新的新闻、天气、资讯等实时信息。当用户询问需要联网才能获取的实时内容时调用。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "要搜索的关键词或问题"
                }
              },
              "required": ["query"]
            }
            """;

        public Task<string> ExecuteAsync(string argumentsJson)
        {
            // 占位实现：联网搜索后端暂未接入。接入后在此处调用真实搜索服务并返回结构化结果。
            return Task.FromResult("联网搜索后端暂未接入，目前无法获取实时搜索结果。");
        }
    }
}
