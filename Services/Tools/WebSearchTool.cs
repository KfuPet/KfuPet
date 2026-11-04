using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 联网搜索工具：复用当前生效模型的服务商 API 密钥，调用服务商官方的搜索接口，
    /// 把结构化结果整理成文本回填给模型。目前适配 DeepSeek 与 Kimi（Moonshot）两家，
    /// 其余服务商返回明确提示，绝不编造搜索结果。
    /// </summary>
    internal class WebSearchTool : ITool
    {
        private const int MaxResults = 5;
        private const int MaxSummaryLength = 400;
        private const int MaxErrorMessageLength = 200;
        private const int RequestTimeoutSeconds = 15;
        private const int SearchTimeoutSeconds = 10;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
        };

        private readonly ModelConfigService _modelConfigService;

        /// <summary>用模型配置服务构造，用于定位当前正在使用的模型及其服务商。</summary>
        /// <param name="modelConfigService">本地模型配置服务。</param>
        public WebSearchTool(ModelConfigService modelConfigService)
        {
            _modelConfigService = modelConfigService;
        }

        public string Name => "web_search";

        public string Description =>
            "联网搜索最新的新闻、天气、资讯等实时信息。当用户询问需要联网才能获取的实时内容时调用。" +
            "返回结果包含标题、摘要与来源名称；回答时只用文字说明来源，不要输出网址或链接，也不要编造搜索结果。";

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

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            var query = ReadQuery(argumentsJson);
            if (string.IsNullOrWhiteSpace(query))
            {
                return "搜索关键词为空，无法联网搜索。";
            }

            var model = _modelConfigService.Models.FirstOrDefault(m => m.IsActive);
            if (model == null)
            {
                return "当前没有正在使用的模型，无法联网搜索。请先在设置中添加并启用一个模型。";
            }

            var backend = ResolveBackend(model.BaseUrl);
            if (backend == null)
            {
                return $"当前使用的模型服务商「{model.ModelName}」未提供联网搜索接口；联网搜索目前支持 DeepSeek 与 Kimi 模型。";
            }

            if (string.IsNullOrWhiteSpace(model.ApiKey))
            {
                return "当前模型未填写 API 密钥，无法联网搜索。";
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, backend.Value.Endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
                request.Content = new StringContent(
                    BuildRequestBody(backend.Value.Format, query), Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var message = ReadErrorMessage(body);
                    Log.Warning($"[搜索] {backend.Value.DisplayName} 搜索失败：HTTP {(int)response.StatusCode} {message}");
                    return $"联网搜索失败（HTTP {(int)response.StatusCode}）：{message}";
                }

                return ParseResponse(backend.Value, body, query);
            }
            catch (Exception ex)
            {
                // 网络异常、超时或响应解析失败都按搜索失败处理，让模型如实告知用户
                Log.Warning($"[搜索] {backend.Value.DisplayName} 搜索异常：{ex.Message}");
                return $"联网搜索失败：{ex.Message}";
            }
        }

        /// <summary>读取模型传入的 query 参数；参数缺失或格式错误时返回空串。</summary>
        private static string ReadQuery(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                return document.RootElement.TryGetProperty("query", out var queryElement) &&
                       queryElement.ValueKind == JsonValueKind.String
                    ? queryElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 按 Base URL 的域名识别服务商：支持 DeepSeek 与 Kimi（Moonshot）官方搜索接口。
        /// 搜索端点由配置里的地址推导，不额外保存第二套密钥或服务地址。
        /// </summary>
        private static SearchBackend? ResolveBackend(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            var endpoint = new Uri(uri, "/v1/tools/search").ToString();
            return uri.Host.ToLowerInvariant() switch
            {
                "api.deepseek.com" => new SearchBackend(endpoint, SearchFormat.DeepSeek, "DeepSeek"),
                "api.moonshot.cn" => new SearchBackend(endpoint, SearchFormat.Kimi, "Kimi"),
                "api.moonshot.ai" => new SearchBackend(endpoint, SearchFormat.Kimi, "Kimi"),
                _ => null
            };
        }

        /// <summary>按服务商构造搜索请求体：DeepSeek 使用 query/count，Kimi 使用 text_query/limit。</summary>
        private static string BuildRequestBody(SearchFormat format, string query)
        {
            if (format == SearchFormat.DeepSeek)
            {
                return JsonSerializer.Serialize(new
                {
                    query,
                    count = MaxResults,
                    need_content = false
                });
            }

            return JsonSerializer.Serialize(new
            {
                text_query = query,
                limit = MaxResults,
                include_content = false,
                timeout_seconds = SearchTimeoutSeconds
            });
        }

        /// <summary>把服务商返回的搜索结果数组整理成模型易读的文本。</summary>
        private static string ParseResponse(SearchBackend backend, string json, string query)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var resultsName = backend.Format == SearchFormat.DeepSeek ? "results" : "search_results";
            if (!root.TryGetProperty(resultsName, out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return $"没有找到与「{query}」相关的网页结果。";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"联网搜索结果（{backend.DisplayName} 搜索）：");

            var index = 0;
            foreach (var item in results.EnumerateArray())
            {
                if (index >= MaxResults)
                {
                    break;
                }

                index++;
                var title = ReadString(item, "title");

                // DeepSeek 的长摘要更适合给模型阅读；Kimi 只提供 snippet
                var summary = backend.Format == SearchFormat.DeepSeek
                    ? ReadString(item, "summary")
                    : ReadString(item, "snippet");
                if (summary.Length == 0)
                {
                    summary = ReadString(item, "snippet");
                }

                var siteName = ReadString(item, "site_name");
                var date = ReadString(item, "date");

                builder.Append(index).Append(". ").AppendLine(title.Length > 0 ? title : "（无标题）");
                if (summary.Length > 0)
                {
                    builder.Append("   摘要：").AppendLine(Truncate(summary, MaxSummaryLength));
                }

                if (siteName.Length > 0 || date.Length > 0)
                {
                    builder.Append("   来源：");
                    if (siteName.Length > 0)
                    {
                        builder.Append(siteName);
                    }

                    if (date.Length > 0)
                    {
                        builder.Append('（').Append(date).Append('）');
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>从错误响应里提取 error.message；解析失败时返回通用提示。</summary>
        private static string ReadErrorMessage(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    var text = message.GetString()?.Trim() ?? string.Empty;
                    if (text.Length > 0)
                    {
                        return Truncate(text, MaxErrorMessageLength);
                    }
                }
            }
            catch (Exception)
            {
                // 非 JSON 错误响应按通用提示处理
            }

            return "服务暂时不可用，请稍后再试。";
        }

        /// <summary>读取对象的字符串属性；缺失或类型不符时返回空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        /// <summary>按字符数截断过长文本，避免搜索结果占用过多上下文。</summary>
        private static string Truncate(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : text[..maxLength] + "…";
        }

        /// <summary>搜索接口的服务商请求格式。</summary>
        private enum SearchFormat
        {
            DeepSeek,
            Kimi
        }

        /// <summary>识别出的搜索后端：端点地址 + 请求格式 + 展示名称。</summary>
        private readonly record struct SearchBackend(string Endpoint, SearchFormat Format, string DisplayName);
    }
}
