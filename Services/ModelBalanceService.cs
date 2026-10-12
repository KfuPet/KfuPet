using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>
    /// 查询模型服务商的账户余额。余额接口不属于 OpenAI 兼容标准，
    /// 因此只对已知服务商（DeepSeek / Moonshot）按其各自端点适配，其余服务商返回 null。
    /// </summary>
    internal static class ModelBalanceService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// 查询指定模型的账户余额，成功返回形如“¥12.34”的展示文本；
        /// 服务商不支持或查询失败时返回 null（调用方显示“未知”）。
        /// </summary>
        /// <param name="baseUrl">模型配置里的 API 基础地址，用于识别服务商。</param>
        /// <param name="apiKey">明文 API 密钥。</param>
        public static async Task<string?> QueryAsync(string baseUrl, string apiKey)
        {
            var target = ResolveTarget(baseUrl);
            if (target == null)
            {
                return null;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target.Value.Endpoint);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"[余额] 查询失败：服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return target.Value.Format == BalanceFormat.DeepSeek
                    ? ParseDeepSeek(json)
                    : ParseMoonshot(json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[余额] 查询失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>按 Base URL 的域名识别服务商，得到余额端点与解析方式；未知服务商返回 null。</summary>
        private static BalanceTarget? ResolveTarget(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri.Host.ToLowerInvariant() switch
            {
                "api.deepseek.com" => new BalanceTarget("https://api.deepseek.com/user/balance", BalanceFormat.DeepSeek),
                "api.moonshot.cn" => new BalanceTarget("https://api.moonshot.cn/v1/users/me/balance", BalanceFormat.Moonshot),
                _ => null
            };
        }

        /// <summary>解析 DeepSeek 余额响应：balance_infos[].total_balance 与 currency。</summary>
        private static string? ParseDeepSeek(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("balance_infos", out var infos) ||
                infos.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var info in infos.EnumerateArray())
            {
                if (info.TryGetProperty("total_balance", out var balance) &&
                    TryGetDecimal(balance, out var amount))
                {
                    var currency = info.TryGetProperty("currency", out var c) ? c.GetString() : null;
                    return FormatBalance(currency, amount);
                }
            }

            return null;
        }

        /// <summary>解析 Moonshot 余额响应：data.available_balance（人民币）。</summary>
        private static string? ParseMoonshot(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("available_balance", out var balance) ||
                !TryGetDecimal(balance, out var amount))
            {
                return null;
            }

            return FormatBalance("CNY", amount);
        }

        /// <summary>把币种与金额拼成展示文本，例如“¥12.34”。</summary>
        private static string FormatBalance(string? currencyCode, decimal amount)
        {
            var symbol = currencyCode?.ToUpperInvariant() switch
            {
                "CNY" => "¥",
                "USD" => "$",
                null or "" => "¥",
                _ => currencyCode + " "
            };
            return $"{symbol}{amount.ToString("0.00", CultureInfo.InvariantCulture)}";
        }

        /// <summary>兼容字符串（DeepSeek）与数字（Moonshot）两种金额表示。</summary>
        private static bool TryGetDecimal(JsonElement element, out decimal value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.TryGetDecimal(out value);
                case JsonValueKind.String:
                    return decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                default:
                    value = 0;
                    return false;
            }
        }

        /// <summary>余额端点的解析方式。</summary>
        private enum BalanceFormat
        {
            DeepSeek,
            Moonshot
        }

        /// <summary>识别出的余额查询目标：端点地址 + 解析方式。</summary>
        private readonly record struct BalanceTarget(string Endpoint, BalanceFormat Format);
    }
}