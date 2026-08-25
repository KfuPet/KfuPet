using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 调用 OpenAI 兼容的 /embeddings 端点，把文本转成向量，
    /// 供长期记忆的语义检索使用。
    /// </summary>
    internal class EmbeddingService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// 将一段文本编码为向量；失败时抛出异常。
        /// </summary>
        public async Task<float[]> EmbedAsync(ModelConfig model, string text)
        {
            if (string.IsNullOrWhiteSpace(model.EmbeddingModelId))
            {
                throw new InvalidOperationException("未配置 Embedding 模型 ID，无法生成向量");
            }

            var endpoint = model.BaseUrl.Trim().TrimEnd('/') + "/embeddings";

            var payload = new
            {
                model = model.EmbeddingModelId,
                input = text
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
                    $"Embedding 请求失败 {(int)response.StatusCode}：{ExtractErrorMessage(body)}");
            }

            return ExtractVector(body);
        }

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

        private static float[] ExtractVector(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var embedding = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

            var vector = new float[embedding.GetArrayLength()];
            var i = 0;
            foreach (var item in embedding.EnumerateArray())
            {
                vector[i++] = item.GetSingle();
            }

            if (vector.Length == 0)
            {
                throw new InvalidOperationException("Embedding 响应为空");
            }

            return vector;
        }
    }
}
