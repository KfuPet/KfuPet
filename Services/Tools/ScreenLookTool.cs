using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 屏幕观察工具：截取前台窗口或主屏画面，交给多模态模型回答模型自己提出的问题，把答案文本回填。
    /// 是否可用由 SettingsService.VisionEnabled 总开关控制；工具内部再做冷却，避免模型频繁截图。
    /// </summary>
    internal class ScreenLookTool : ITool
    {
        /// <summary>两次截图之间的最小间隔（秒），防止模型每句话都看一次屏幕。</summary>
        private const int CooldownSeconds = 60;

        /// <summary>单次问题长度上限，避免模型把整段长文塞进参数。</summary>
        private const int MaxQuestionLength = 200;

        /// <summary>全局上次截图时间：所有工具实例共享同一冷却窗口。</summary>
        private static DateTime _lastLookTimeUtc = DateTime.MinValue;

        private readonly ModelConfigService _modelConfigService;

        /// <summary>实际执行截图与视觉问答：由主窗口注入，直接复用 VisionService 的截图与问答流程。</summary>
        private readonly Func<ModelConfig, string, Task<string>> _lookAsync;

        /// <summary>用模型配置服务与观察委托构造。</summary>
        /// <param name="modelConfigService">本地模型配置服务，用于定位当前生效模型。</param>
        /// <param name="lookAsync">执行截图与视觉问答的委托：参数为模型与问题，返回画面描述。</param>
        public ScreenLookTool(
            ModelConfigService modelConfigService,
            Func<ModelConfig, string, Task<string>> lookAsync)
        {
            _modelConfigService = modelConfigService;
            _lookAsync = lookAsync;
        }

        public string Name => "screen_look";

        public string Description =>
            "查看主人当前的电脑屏幕画面。当主人提到「你看这个」「屏幕上这是什么」「我现在在做什么」，" +
            "或需要屏幕上才能看到的信息时调用。每次只问一个具体问题，返回的是对画面的文字描述，" +
            "你本身看不到图片。不要连续调用，也不要在没有必要时调用。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "question": {
                  "type": "string",
                  "description": "希望从屏幕画面中了解的具体问题，例如「屏幕上显示的报错是什么」"
                }
              },
              "required": ["question"]
            }
            """;

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            var question = ReadQuestion(argumentsJson);
            if (string.IsNullOrWhiteSpace(question))
            {
                return "需要提供想从屏幕画面中了解的具体问题。";
            }

            if (!SettingsService.Instance.VisionEnabled)
            {
                return "主人还没有允许我看屏幕。";
            }

            var model = _modelConfigService.Models.FirstOrDefault(m => m.IsActive);
            if (model == null)
            {
                return "当前没有正在使用的模型，无法查看屏幕。";
            }

            var now = DateTime.UtcNow;
            if ((now - _lastLookTimeUtc).TotalSeconds < CooldownSeconds)
            {
                return "刚刚已经看过屏幕了，请稍等一会儿再问。";
            }

            _lastLookTimeUtc = now;

            try
            {
                var reply = await _lookAsync(model, question);
                return string.IsNullOrWhiteSpace(reply) ? "没有看清屏幕上的内容。" : reply;
            }
            catch (Exception ex)
            {
                Log.Warning($"[视觉] 查看屏幕失败：{ex.Message}");
                return $"查看屏幕失败：{ex.Message}";
            }
        }

        /// <summary>读取模型传入的 question 参数；缺失或格式错误时返回空串。</summary>
        private static string ReadQuestion(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                var question = document.RootElement.TryGetProperty("question", out var element) &&
                               element.ValueKind == JsonValueKind.String
                    ? element.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                return question.Length <= MaxQuestionLength
                    ? question
                    : question[..MaxQuestionLength];
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
