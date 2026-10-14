using System.Text.Json;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 投喂工具：主人明确表示要喂东西给你吃时由模型调用，
    /// 复用 <see cref="EmotionService"/> 结算一次数值变化（愉悦 / 精力 / 亲密上升），
    /// 无物品系统——投喂只做一次情绪结算，不扣减任何道具。
    /// </summary>
    internal class FeedTool : ITool
    {
        /// <summary>一次投喂带来的愉悦度提升。</summary>
        private const double FeedJoy = 5;

        /// <summary>一次投喂带来的精力提升。</summary>
        private const double FeedEnergy = 8;

        /// <summary>一次投喂带来的亲密度提升。</summary>
        private const double FeedIntimacy = 3;

        public string Name => "feed";

        public string Description =>
            "接受主人投喂的食物，吃完后心情、精力与亲密度都会变好。" +
            "只有当主人明确表示要喂你吃东西、给你零食、请你吃饭时才调用；" +
            "主人只是提到食物、或自己在吃饭时不要调用。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "food": {
                  "type": "string",
                  "description": "主人投喂的食物名称，例如“小鱼干”；主人没具体说时留空"
                }
              },
              "additionalProperties": false
            }
            """;

        public Task<string> ExecuteAsync(string argumentsJson)
        {
            var food = ReadFood(argumentsJson);
            var reason = string.IsNullOrWhiteSpace(food) ? "主人投喂" : $"主人投喂了{food}";
            EmotionService.Instance.Adjust(FeedJoy, FeedEnergy, FeedIntimacy, reason);

            return Task.FromResult(string.IsNullOrWhiteSpace(food)
                ? "吃得饱饱的，心情和精力都变好了，也更亲近主人了。"
                : $"吃到了{food}，心情和精力都变好了，也更亲近主人了。");
        }

        /// <summary>从工具参数里取出食物名称；未提供或解析失败返回空串。</summary>
        private static string ReadFood(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.TryGetProperty("food", out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[情绪] 投喂工具参数解析失败：{ex.Message}");
            }

            return string.Empty;
        }
    }
}
