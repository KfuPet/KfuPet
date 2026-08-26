using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 与当前启用的 AI 模型对话：调用 OpenAI 兼容的 /chat/completions 端点。
    /// 本类只负责发送请求并解析回复；系统提示词与记忆上下文由调用方组装后传入。
    /// </summary>
    internal class ChatService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// 发送对话请求：使用调用方提供的系统提示词 + 历史 + 当前用户消息，返回 AI 回复文本。
        /// </summary>
        /// <param name="history">短期记忆（会话上下文），不含当前用户消息。</param>
        public async Task<string> SendAsync(
            ModelConfig model, string systemPrompt, IReadOnlyList<ChatMessage> history, string userMessage)
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };
            messages.AddRange(history);
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

            return await SendRawAsync(model, messages);
        }

        /// <summary>
        /// 发送消息列表并返回第一条回复文本，供记忆分析等内部流程复用。
        /// </summary>
        internal async Task<string> SendRawAsync(ModelConfig model, IReadOnlyList<ChatMessage> messages)
        {
            var endpoint = model.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";

            var payload = new
            {
                model = model.ModelId,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList()
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
