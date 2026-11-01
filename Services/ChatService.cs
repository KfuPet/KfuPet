using System.Diagnostics;
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

        /// <summary>发送对话请求：使用调用方提供的系统提示词 + 历史 + 当前用户消息，返回 AI 回复文本。</summary>
        /// <param name="history">短期记忆（会话上下文），不含当前用户消息。</param>
        /// <param name="category">调用来源分类，用于 token 用量统计。</param>
        public async Task<string> SendAsync(
            ModelConfig model, string systemPrompt, IReadOnlyList<ChatMessage> history, string userMessage,
            TokenUsageCategory category = TokenUsageCategory.Other,
            ModelCallKind kind = ModelCallKind.None)
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };
            messages.AddRange(history);
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

            return await SendRawAsync(model, messages, category, kind: kind);
        }

        /// <summary>
        /// 发送消息列表并返回第一条回复文本，供记忆分析等内部流程复用（不带工具）。
        /// </summary>
        internal async Task<string> SendRawAsync(
            ModelConfig model, IReadOnlyList<ChatMessage> messages,
            TokenUsageCategory category = TokenUsageCategory.Other,
            CancellationToken cancellationToken = default,
            ModelCallKind kind = ModelCallKind.None)
        {
            if (kind != ModelCallKind.None &&
                SettingsService.Instance.GetModelSource(kind) == ModelCallSource.Local)
            {
                var localReply = await TryLocalInferAsync(kind, messages, cancellationToken);
                if (localReply != null)
                {
                    return localReply;
                }
            }

            var payload = BuildPayload(model, messages, null);
            var body = await PostAsync(model, payload, category, cancellationToken);
            return ExtractReply(body);
        }

        /// <summary>
        /// 尝试用本地模型生成回复；未启用、模型能力不足或尚未加载时返回 null（本次回退远端）。
        /// 首次碰到“已启用但未加载”时会请求后台加载，本次调用仍走远端。
        /// </summary>
        private static async Task<string?> TryLocalInferAsync(
            ModelCallKind kind, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        {
            var service = LocalModelService.Instance;
            var active = service.GetActiveModel();
            if (active == null)
            {
                Log.Debug($"[本地模型] {kind} 本次走远端（尚未启用本地模型）");
                return null;
            }

            if (!CanLocalHandle(kind, active))
            {
                Log.Warning($"[本地模型] {kind} 本次走远端（{active.ModelName} 不支持该功能）");
                return null;
            }

            if (!service.TryGetReadyRuntime(out var runtime))
            {
                service.RequestWarmUp();
                Log.Debug($"[本地模型] {kind} 本次走远端（本地模型尚未加载，已请求后台加载）");
                return null;
            }

            // 短句调用不排队等正在进行的记忆提炼：直接回退远端，保证桌宠反应不卡顿
            if (IsShortReply(kind) && runtime.IsBusy)
            {
                Log.Debug($"[本地模型] 短句推理正忙，本次走远端");
                return null;
            }

            var (systemPrompt, userMessage) = SplitLocalPrompt(messages);
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return null;
            }

            var maxTokens = GetLocalMaxTokens(kind);
            var timeout = GetLocalTimeout(kind);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            Log.Info($"[本地模型] {kind} 开始推理：最多 {maxTokens} tokens，超时 {timeout.TotalSeconds:0} 秒");
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await Task.Run(
                    () => runtime.InferAsync(systemPrompt, userMessage, maxTokens, timeoutCts.Token),
                    timeoutCts.Token);
                stopwatch.Stop();
                Log.Info($"[本地模型] {kind} 推理完成：{stopwatch.ElapsedMilliseconds} ms，输出 {reply.Length} 字");
                return reply;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning($"[本地模型] {kind} 推理超时（{timeout.TotalSeconds:0} 秒），本次回退远端");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[本地模型] {kind} 推理失败，本次回退远端：{ex.Message}");
                return null;
            }
        }

        /// <summary>按模型目录的能力标记判断该模型能否承担这类调用。</summary>
        private static bool CanLocalHandle(ModelCallKind kind, DeployedModel active)
        {
            return kind switch
            {
                ModelCallKind.MemoryExtraction or ModelCallKind.MemoryArchiveExtraction =>
                    LocalModelCatalog.SupportsMemoryExtraction(active),
                ModelCallKind.MemorySummary =>
                    LocalModelCatalog.SupportsMemorySummarization(active),
                _ => true
            };
        }

        /// <summary>是否为短句类调用：这类调用不与后台记忆提炼排队。</summary>
        private static bool IsShortReply(ModelCallKind kind)
        {
            return kind is ModelCallKind.TouchReaction or ModelCallKind.UpdateNotification
                or ModelCallKind.SnapBack or ModelCallKind.ProactiveChat or ModelCallKind.EmotionJudge;
        }

        /// <summary>把消息拆成系统提示与单段用户文本；多轮历史按主人/桌宠前缀拼接。</summary>
        private static (string SystemPrompt, string UserMessage) SplitLocalPrompt(
            IReadOnlyList<ChatMessage> messages)
        {
            var systemPrompt = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? string.Empty;
            var turns = messages
                .Where(m => m.Role != "system" && !string.IsNullOrWhiteSpace(m.Content))
                .ToList();

            var userMessage = turns.Count <= 1
                ? turns.FirstOrDefault()?.Content ?? string.Empty
                : string.Join("\n", turns.Select(m =>
                    m.Role == "assistant" ? $"桌宠：{m.Content}" : $"主人：{m.Content}"));

            return (systemPrompt, userMessage);
        }

        /// <summary>按用途给出本地推理的最大输出 token 数。</summary>
        private static int GetLocalMaxTokens(ModelCallKind kind)
        {
            return kind switch
            {
                ModelCallKind.MemoryExtraction or ModelCallKind.MemoryArchiveExtraction => 512,
                ModelCallKind.MemorySummary => 128,
                ModelCallKind.MemoryScoring => 32,
                _ => 96
            };
        }

        /// <summary>按用途给出本地推理超时；超时后本次回退远端，不影响功能。</summary>
        private static TimeSpan GetLocalTimeout(ModelCallKind kind)
        {
            return kind switch
            {
                ModelCallKind.MemoryExtraction or ModelCallKind.MemoryArchiveExtraction => TimeSpan.FromSeconds(180),
                ModelCallKind.MemorySummary => TimeSpan.FromSeconds(90),
                ModelCallKind.MemoryScoring => TimeSpan.FromSeconds(30),
                _ => TimeSpan.FromSeconds(20)
            };
        }

        /// <summary>带工具调用能力的对话：请求携带工具定义，模型返回工具调用时本地执行并回填结果，
        /// 多轮循环直到模型给出最终文本；模型不支持工具时自动降级为普通对话。</summary>
        /// <param name="history">短期记忆（会话上下文），不含当前用户消息。</param>
        /// <param name="tools">提供给模型的工具定义列表。</param>
        /// <param name="executeToolAsync">工具执行器：接收（工具名，参数 JSON），返回结果文本。</param>
        /// <param name="category">调用来源分类，用于 token 用量统计。</param>
        public async Task<string> SendWithToolsAsync(
            ModelConfig model,
            string systemPrompt,
            IReadOnlyList<ChatMessage> history,
            string userMessage,
            IReadOnlyList<ToolDefinition> tools,
            Func<string, string, Task<string>> executeToolAsync,
            TokenUsageCategory category = TokenUsageCategory.Conversation)
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };
            messages.AddRange(history);
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

            Log.Debug($"[对话] 发起请求：模型 {model.ModelId}，历史 {history.Count} 条，" +
                      $"工具 {tools.Count} 个，输入 {userMessage.Length} 字");

            try
            {
                return await RunToolLoopAsync(model, messages, tools, executeToolAsync, category);
            }
            catch (InvalidOperationException ex) when (IsToolUnsupportedError(ex.Message))
            {
                // 模型不支持工具调用（或工具相关参数错误）时，降级为普通对话。
                Log.Warning($"[对话] 工具调用不可用，降级为普通对话：{ex.Message}");
                return await SendRawAsync(model, messages, category);
            }
        }

        /// <summary>工具调用循环：请求 → 执行工具 → 回填结果，直到模型返回纯文本或超过轮次上限。</summary>
        private async Task<string> RunToolLoopAsync(
            ModelConfig model,
            List<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            Func<string, string, Task<string>> executeToolAsync,
            TokenUsageCategory category)
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var response = await SendMessagesAsync(model, messages, tools, category);

                if (response.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(response.Content))
                    {
                        throw new InvalidOperationException("响应格式无法识别");
                    }
                    Log.Debug($"[对话] 第 {round + 1} 轮拿到最终回复：{response.Content.Length} 字");
                    return response.Content.Trim();
                }

                Log.Info($"[对话] 第 {round + 1} 轮请求调用 {response.ToolCalls.Count} 个工具：" +
                         string.Join("、", response.ToolCalls.Select(c => c.Name)));

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
                        Log.Debug($"[对话] 工具 {call.Name} 执行完成，返回 {result.Length} 字");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[对话] 工具 {call.Name} 执行失败：{ex.Message}");
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

            Log.Warning($"[对话] 工具调用超过 {MaxToolRounds} 轮，已中止");
            throw new InvalidOperationException("工具调用轮次过多，已中止");
        }

        /// <summary>发送带工具的消息列表并解析回复（文本 + 工具调用）。</summary>
        private async Task<ChatResponse> SendMessagesAsync(
            ModelConfig model, IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition>? tools, TokenUsageCategory category)
        {
            var payload = BuildPayload(model, messages, tools);
            var body = await PostAsync(model, payload, category);
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
        private async Task<string> PostAsync(
            ModelConfig model, JsonObject payload, TokenUsageCategory category, CancellationToken cancellationToken = default)
        {
            var endpoint = model.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";
            var stopwatch = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            }
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}：{ExtractErrorMessage(body)}";
                Log.Error($"[对话] 请求失败（{stopwatch.ElapsedMilliseconds} ms）：{reason}");
                throw new InvalidOperationException(reason);
            }

            Log.Debug($"[对话] 请求成功：HTTP {(int)response.StatusCode}，" +
                      $"耗时 {stopwatch.ElapsedMilliseconds} ms，响应 {body.Length} 字符");

            // 记录本次调用的 token 用量（服务商未返回 usage 时按 0 计，仅累计请求次数）
            var usage = ExtractUsage(body);
            TokenUsageService.Instance.Record(
                model, category, usage?.Prompt ?? 0, usage?.Completion ?? 0);

            return body;
        }

        /// <summary>
        /// 从 OpenAI 兼容响应中解析 usage（prompt_tokens / completion_tokens）；缺失时返回 null。
        /// </summary>
        private static (long Prompt, long Completion)? ExtractUsage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("usage", out var usage) ||
                    usage.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt64(out var promptValue)
                    ? promptValue
                    : 0;
                var completion = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt64(out var completionValue)
                    ? completionValue
                    : 0;

                return (prompt, completion);
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
