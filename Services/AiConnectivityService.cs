using System.Net.Http.Headers;
using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>
    /// 访问 OpenAI 兼容的 /models 端点：既能通过是否返回成功状态码验证模型配置的连通性，
    /// 也能拉取服务商当前提供的模型 ID 列表。
    /// </summary>
    internal class AiConnectivityService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>发起一次最小化请求验证连通性，失败时抛出异常。</summary>
        /// <param name="baseUrl">API 基础地址，例如 https://api.openai.com/v1。</param>
        /// <param name="apiKey">API 密钥，可为空（部分本地服务无需鉴权）。</param>
        public async Task TestAsync(string baseUrl, string apiKey)
        {
            using var request = CreateModelsRequest(baseUrl, apiKey);
            using var response = await HttpClient.SendAsync(request);
            EnsureSuccess(response);
        }

        /// <summary>拉取服务商当前提供的模型 ID 列表（解析 /models 返回的 data[].id），失败时抛出异常。</summary>
        /// <param name="baseUrl">API 基础地址，例如 https://api.openai.com/v1。</param>
        /// <param name="apiKey">API 密钥，可为空（部分本地服务无需鉴权）。</param>
        public async Task<IReadOnlyList<string>> GetModelIdsAsync(string baseUrl, string apiKey)
        {
            using var request = CreateModelsRequest(baseUrl, apiKey);
            using var response = await HttpClient.SendAsync(request);
            EnsureSuccess(response);

            var json = await response.Content.ReadAsStringAsync();
            var ids = new List<string>();

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        var value = id.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            ids.Add(value);
                        }
                    }
                }
            }

            return ids;
        }

        /// <summary>
        /// 构造指向 {BaseUrl}/models 的 GET 请求，按需带上 Bearer 鉴权头。
        /// </summary>
        private static HttpRequestMessage CreateModelsRequest(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("Base URL 不能为空");
            }

            var endpoint = baseUrl.Trim().TrimEnd('/') + "/models";
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            return request;
        }

        /// <summary>
        /// 校验响应状态码，非成功时抛出带状态码信息的异常。
        /// </summary>
        private static void EnsureSuccess(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
    }
}