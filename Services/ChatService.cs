using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 与当前启用的 AI 模型对话：调用 OpenAI 兼容的 /chat/completions 端点，
    /// 系统提示词由 PromptService 组装（全局 + 角色）。
    /// </summary>
    internal class ChatService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly PromptService _promptService = new();

        /// <summary>
        /// 发送一条用户消息并返回 AI 回复文本，失败时抛出异常。
        /// </summary>
        public async Task<string> SendAsync(ModelConfig model, string userMessage)
        {
            var endpoint = model.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";

            var payload = new
            {
                model = model.ModelId,
                messages = new[]
                {
                    new { role = "system", content = _promptService.BuildSystemPrompt() },
                    new { role = "user", content = userMessage }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            }
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}：{ExtractErrorMessage(body)}");
            }

            return ExtractReply(body);
        }

        /// <summary>
        /// 从错误响应中提取服务商返回的错误信息（OpenAI 兼容的 error.message），
        /// 无法解析时返回截断的原始响应，便于定位 400 类参数错误。
        /// </summary>
        private static string ExtractErrorMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? body;
                }
            }
            catch (JsonException)
            {
                // 非 JSON 响应，落入下方截断返回
            }

            return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
        }

        /// <summary>
        /// 从 OpenAI 兼容响应中取出第一条回复文本。
        /// </summary>
        private static string ExtractReply(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content.Trim();
                }
            }
            catch (JsonException)
            {
                // 落入下方统一报错
            }

            throw new InvalidOperationException("响应格式无法识别");
        }
    }
}
