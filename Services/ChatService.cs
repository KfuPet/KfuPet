using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KfuPet.Models;
using KfuPet.Services.Tools;

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

        /// <summary>工具调用循环的最大轮次，防止模型反复请求工具导致死循环。</summary>
        private const int MaxToolRounds = 3;

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
        /// 发送消息列表并返回第一条回复文本，供记忆分析等内部流程复用（不带工具）。
        /// </summary>
        internal async Task<string> SendRawAsync(ModelConfig model, IReadOnlyList<ChatMessage> messages)
        {
            var payload = BuildPayload(model, messages, null);
            var body = await PostAsync(model, payload);
            return ExtractReply(body);
        }

        /// <summary>
        /// 带工具调用能力的对话：请求携带工具定义，模型返回工具调用时本地执行并回填结果，
        /// 多轮循环直到模型给出最终文本；模型不支持工具时自动降级为普通对话。
        /// </summary>
        /// <param name="history">短期记忆（会话上下文），不含当前用户消息。</param>
        /// <param name="tools">提供给模型的工具定义列表。</param>
        /// <param name="executeToolAsync">工具执行器：接收（工具名，参数 JSON），返回结果文本。</param>
        public async Task<string> SendWithToolsAsync(
            ModelConfig model,
            string systemPrompt,
            IReadOnlyList<ChatMessage> history,
            string userMessage,
            IReadOnlyList<ToolDefinition> tools,
            Func<string, string, Task<string>> executeToolAsync)
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };
            messages.AddRange(history);
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

            try
            {
                return await RunToolLoopAsync(model, messages, tools, executeToolAsync);
            }
            catch (InvalidOperationException ex) when (IsToolUnsupportedError(ex.Message))
            {
                // 模型不支持工具调用（或工具相关参数错误）时，降级为普通对话。
                return await SendRawAsync(model, messages);
            }
        }

        /// <summary>工具调用循环：请求 → 执行工具 → 回填结果，直到模型返回纯文本或超过轮次上限。</summary>
        private async Task<string> RunToolLoopAsync(
            ModelConfig model,
            List<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            Func<string, string, Task<string>> executeToolAsync)
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var response = await SendMessagesAsync(model, messages, tools);

                if (response.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(response.Content))
                    {
                        throw new InvalidOperationException("响应格式无法识别");
                    }
                    return response.Content.Trim();
                }

                // 记录 assistant 的工具调用请求
                messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = response.Content,
                    ToolCalls = response.ToolCalls
                });

                // 逐个执行工具并把结果回填
                foreach (var call in response.ToolCalls)
                {
                    string result;
                    try
                    {
                        result = await executeToolAsync(call.Name, call.Arguments);
                    }
                    catch (Exception ex)
                    {
                        result = "工具执行失败：" + ex.Message;
                    }

                    messages.Add(new ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = call.Id,
                        Content = result
                    });
                }
            }

            throw new InvalidOperationException("工具调用轮次过多，已中止");
        }

        /// <summary>发送带工具的消息列表并解析回复（文本 + 工具调用）。</summary>
        private async Task<ChatResponse> SendMessagesAsync(
            ModelConfig model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools)
        {
            var payload = BuildPayload(model, messages, tools);
            var body = await PostAsync(model, payload);
            return ExtractResponse(body);
        }

        /// <summary>构建 /chat/completions 请求体。</summary>
        private static JsonObject BuildPayload(
            ModelConfig model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools)
        {
            var payload = new JsonObject
            {
                ["model"] = model.ModelId,
                ["messages"] = BuildMessagesJson(messages)
            };

            if (tools is { Count: > 0 })
            {
                payload["tools"] = BuildToolsJson(tools);
            }

            return payload;
        }

        /// <summary>把消息列表转成 OpenAI 兼容的 messages 数组。</summary>
        private static JsonArray BuildMessagesJson(IReadOnlyList<ChatMessage> messages)
        {
            var array = new JsonArray();
            foreach (var m in messages)
            {
                var obj = new JsonObject { ["role"] = m.Role };

                if (m.ToolCalls is { Count: > 0 })
                {
                    var toolCalls = new JsonArray();
                    foreach (var tc in m.ToolCalls)
                    {
                        toolCalls.Add(new JsonObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = tc.Name,
                                ["arguments"] = tc.Arguments
                            }
                        });
                    }
                    obj["tool_calls"] = toolCalls;
                    if (m.Content != null)
                    {
                        obj["content"] = m.Content;
                    }
                }
                else if (m.Role == "tool")
                {
                    obj["tool_call_id"] = m.ToolCallId ?? string.Empty;
                    obj["content"] = m.Content ?? string.Empty;
                }
                else
                {
                    obj["content"] = m.Content ?? string.Empty;
                }

                array.Add(obj);
            }
            return array;
        }

        /// <summary>把工具定义列表转成 OpenAI 兼容的 tools 数组。</summary>
        private static JsonArray BuildToolsJson(IReadOnlyList<ToolDefinition> tools)
        {
            var array = new JsonArray();
            foreach (var t in tools)
            {
                array.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = JsonNode.Parse(t.ParametersJson)
                    }
                });
            }
            return array;
        }

        /// <summary>发起 POST 请求并返回响应体文本；非成功状态码抛出异常。</summary>
        private async Task<string> PostAsync(ModelConfig model, JsonObject payload)
        {
            var endpoint = model.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            }
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}：{ExtractErrorMessage(body)}");
            }

            return body;
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

        /// <summary>
        /// 从 OpenAI 兼容响应中解析回复文本与工具调用列表。
        /// </summary>
        private static ChatResponse ExtractResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var message = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

                string? content = null;
                if (message.TryGetProperty("content", out var contentEl) &&
                    contentEl.ValueKind != JsonValueKind.Null)
                {
                    content = contentEl.GetString();
                }

                var toolCalls = new List<ToolCall>();
                if (message.TryGetProperty("tool_calls", out var toolCallsEl))
                {
                    foreach (var tc in toolCallsEl.EnumerateArray())
                    {
                        var function = tc.GetProperty("function");
                        toolCalls.Add(new ToolCall
                        {
                            Id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                            Name = function.GetProperty("name").GetString() ?? string.Empty,
                            Arguments = function.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }

                return new ChatResponse(content, toolCalls);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("响应格式无法识别");
            }
        }

        /// <summary>判断错误信息是否与工具不支持相关，用于触发降级到普通对话。</summary>
        private static bool IsToolUnsupportedError(string message)
        {
            return message.Contains("tool", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>一次模型响应的解析结果：可选文本 + 工具调用列表。</summary>
        private sealed record ChatResponse(string? Content, List<ToolCall> ToolCalls);
    }
}
