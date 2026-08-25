using System.Net.Http.Headers;

namespace KfuPet.Services
{
    /// <summary>
    /// 验证 AI 模型配置的连通性：调用 OpenAI 兼容的 /models 端点，
    /// 通过是否返回成功状态码判断服务是否可达、API Key 是否有效。
    /// </summary>
    internal class AiConnectivityService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// 发起一次最小化请求验证连通性，失败时抛出异常。
        /// </summary>
        /// <param name="baseUrl">API 基础地址，例如 https://api.openai.com/v1。</param>
        /// <param name="apiKey">API 密钥，可为空（部分本地服务无需鉴权）。</param>
        public async Task TestAsync(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("Base URL 不能为空");
            }

            var endpoint = baseUrl.Trim().TrimEnd('/') + "/models";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
    }
}
