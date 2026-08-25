using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 与当前启用的 AI 模型对话：调用 OpenAI 兼容的 /chat/completions 端点。
    /// 系统提示词由 PromptService 组装（全局 + 角色），并注入检索到的长期记忆；
    /// 同时提供记忆提取能力，把值得记住的信息交给 MemoryManager 落盘。
    /// </summary>
    internal class ChatService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly PromptService _promptService = new();
        private readonly MemoryManager _memoryManager = new();
        private readonly LogService _logService;

        public ChatService(LogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// 发送用户消息并返回 AI 回复文本，失败时抛出异常。
        /// <paramref name="history"/> 为短期记忆（会话上下文），不含当前用户消息。
        /// </summary>
        public async Task<string> SendAsync(
            ModelConfig model, IReadOnlyList<ChatMessage> history, string userMessage)
        {
            var systemPrompt = _promptService.BuildSystemPrompt();

            // 检索相关长期记忆并注入系统提示词
            var memories = await _memoryManager.SearchAsync(model, userMessage, 5);
            if (memories.Count > 0)
            {
                systemPrompt += "\n\n关于主人的记忆（你已知晓）：\n" + MemoryManager.BuildContext(memories);
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };
            messages.AddRange(history);
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

            return await ChatAsync(model, messages);
        }

        /// <summary>
        /// 让模型判断本轮对话是否包含值得长期记住的用户信息。
        /// 返回提取结果，解析失败或无需记忆时返回 null。
        /// </summary>
        public async Task<MemoryExtraction?> ExtractMemoryAsync(
            ModelConfig model, string userMessage, string assistantReply)
        {
            const string extractorPrompt = """
                你是记忆提取器。判断以下对话中是否包含值得长期记住的、关于用户的信息
                （如名字、喜好、习惯、经历、目标、重要关系等）。
                只记住稳定且长期有用的信息，忽略临时、琐碎或纯寒暄的内容。

                请只输出一个 JSON 对象，不要包含其他任何文字或代码块标记，格式如下：
                {"shouldRemember": false, "content": "", "importance": 0.5}
                - shouldRemember: 是否需要记住（true/false）
                - content: 用一句话概括要记住的信息，不需要记住时留空字符串
                - importance: 0 到 1 之间的小数，越高越重要
                """;

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = extractorPrompt },
                new()
                {
                    Role = "user",
                    Content = $"用户说：{userMessage}\n助手回复：{assistantReply}\n\n请判断并输出 JSON。"
                }
            };

            var reply = await ChatAsync(model, messages);
            return ParseExtraction(reply);
        }

        /// <summary>
        /// 提取并存储记忆：让模型判断本轮对话是否值得记住，值得就写入长期记忆。
        /// 内部吞掉所有异常，提取失败不影响对话主流程，但会记录日志便于排查。
        /// </summary>
        public async Task ExtractAndStoreAsync(ModelConfig model, string userMessage, string assistantReply)
        {
            try
            {
                _logService.Debug("[记忆] 开始提取本轮对话中值得记住的信息...");

                var extraction = await ExtractMemoryAsync(model, userMessage, assistantReply);

                if (extraction == null)
                {
                    _logService.Warning("[记忆] 提取结果解析失败或为空，跳过写入");
                    return;
                }

                if (!extraction.ShouldRemember)
                {
                    _logService.Debug("[记忆] 模型判定本轮无需记住");
                    return;
                }

                if (string.IsNullOrWhiteSpace(extraction.Content))
                {
                    _logService.Warning("[记忆] 判定需要记住但内容为空，跳过写入");
                    return;
                }

                await _memoryManager.StoreAsync(model, extraction.Content, extraction.Importance);
                _logService.Info($"[记忆] 已写入：{extraction.Content}（重要性 {extraction.Importance:F2}）");
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 提取失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 发送消息列表并返回第一条回复文本。
        /// </summary>
        private async Task<string> ChatAsync(ModelConfig model, IReadOnlyList<ChatMessage> messages)
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

        private static MemoryExtraction? ParseExtraction(string reply)
        {
            var text = reply.Trim();

            // 剥离可能的 ```json ... ``` 代码块包裹
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var newline = text.IndexOf('\n');
                if (newline >= 0)
                {
                    text = text.Substring(newline + 1).Trim();
                }
                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                {
                    text = text.Substring(0, fence).Trim();
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                var shouldRemember = root.TryGetProperty("shouldRemember", out var s) && s.GetBoolean();
                var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                var importance = root.TryGetProperty("importance", out var i) && i.TryGetDouble(out var d) ? d : 0.5;

                return new MemoryExtraction
                {
                    ShouldRemember = shouldRemember,
                    Content = content.Trim(),
                    Importance = importance
                };
            }
            catch (JsonException)
            {
                return null;
            }
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

    /// <summary>记忆提取结果。</summary>
    public class MemoryExtraction
    {
        /// <summary>是否需要记住。</summary>
        public bool ShouldRemember { get; set; }

        /// <summary>要记住的内容（一句话）。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>重要性，0~1。</summary>
        public double Importance { get; set; } = 0.5;
    }
}
