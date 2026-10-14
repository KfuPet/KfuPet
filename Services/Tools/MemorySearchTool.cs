using System.Text.Json;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 长期记忆检索工具：只在主人问“你还记得……吗”、提起以前聊过的事情、
    /// 或需要回忆主人的某个信息时由模型调用，复用 <see cref="MemorySystem"/> 做本地关键词检索（零模型开销）。
    /// </summary>
    internal class MemorySearchTool : ITool
    {
        /// <summary>单次检索最多返回的记忆条数。</summary>
        private const int MaxResults = 5;

        private readonly MemorySystem _memorySystem;

        /// <summary>用记忆系统构建，用于检索长期记忆。</summary>
        /// <param name="memorySystem">三级记忆系统门面。</param>
        public MemorySearchTool(MemorySystem memorySystem)
        {
            _memorySystem = memorySystem;
        }

        public string Name => "memory_search";

        public string Description =>
            "检索你长期记住的、关于主人的信息（喜好、习惯、经历、重要关系、重要事件等）。" +
            "只有当主人问“你还记得……吗”、提起以前聊过的内容、或需要回忆主人的某个信息时才调用；" +
            "普通闲聊、提问、求助都不需要调用。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "要回忆的关键词或主题，例如“生日”“喜欢的食物”“上次聊的事”"
                }
              },
              "required": ["query"],
              "additionalProperties": false
            }
            """;

        public Task<string> ExecuteAsync(string argumentsJson)
        {
            var query = ReadQuery(argumentsJson);
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult("没有提供要回忆的内容。");
            }

            var hits = _memorySystem.SearchLongTerm(query, MaxResults);
            if (hits.Count == 0)
            {
                return Task.FromResult($"没有找到与「{query}」相关的记忆，可以如实告诉主人你不记得了。");
            }

            var text = string.Join("\n", hits.Select(m => "- " + m));
            return Task.FromResult($"找到以下相关记忆：\n{text}");
        }

        /// <summary>从工具参数里取出检索关键词；解析失败返回空串。</summary>
        private static string ReadQuery(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.TryGetProperty("query", out var query) &&
                    query.ValueKind == JsonValueKind.String)
                {
                    return query.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[记忆] 检索工具参数解析失败：{ex.Message}");
            }

            return string.Empty;
        }
    }
}
