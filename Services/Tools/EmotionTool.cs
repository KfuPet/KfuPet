using System.Text.Json;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 情绪调整工具：只在主人明确表达对桌宠的态度、情绪或关系时由模型调用，
    /// 复用 <see cref="EmotionService"/> 在**同一次请求内**微调情绪，不产生额外的模型请求。
    /// </summary>
    internal class EmotionTool : ITool
    {
        public string Name => "emotion_adjust";

        public string Description =>
            "微调你自己的心情（愉悦 / 精力 / 亲密三个维度）。" +
            "只有当主人明确表达了对你的态度、情绪或关系时才调用——例如夸奖你、责备你、关心你、" +
            "对你冷淡、冲你撒娇，或者直接问“你现在心情怎么样”。" +
            "普通的提问、求助、闲聊寒暄，以及与情绪无关的内容都不要调用本工具。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "joy_delta": {
                  "type": "number",
                  "description": "愉悦度增减，-15 到 15。被夸奖、被关心用正数；被责备、被冷淡用负数。"
                },
                "energy_delta": {
                  "type": "number",
                  "description": "精力增减，-15 到 15。被拉着长时间聊天用负数；被温柔对待用正数。"
                },
                "intimacy_delta": {
                  "type": "number",
                  "description": "亲密度增减，-15 到 15。主人主动亲近、说心里话用正数；冷落、疏远用负数。"
                },
                "reason": {
                  "type": "string",
                  "description": "一句话说明这次调整的原因，用于日志排查。"
                }
              },
              "required": ["reason"],
              "additionalProperties": false
            }
            """;

        public Task<string> ExecuteAsync(string argumentsJson)
        {
            double joy = 0;
            double energy = 0;
            double intimacy = 0;
            var reason = "模型调整心情";

            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                var root = document.RootElement;

                joy = ReadNumber(root, "joy_delta");
                energy = ReadNumber(root, "energy_delta");
                intimacy = ReadNumber(root, "intimacy_delta");

                if (root.TryGetProperty("reason", out var reasonElement) &&
                    reasonElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(reasonElement.GetString()))
                {
                    reason = reasonElement.GetString()!;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[情绪] 工具参数解析失败：{ex.Message}");
                return Task.FromResult("参数格式不正确，本次心情调整已跳过。");
            }

            if (joy == 0 && energy == 0 && intimacy == 0)
            {
                return Task.FromResult("没有需要调整的心情变化。");
            }

            EmotionService.Instance.Adjust(joy, energy, intimacy, $"模型判断：{reason}");
            return Task.FromResult("心情已调整。");
        }

        private static double ReadNumber(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : 0;
        }
    }
}
