using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 从 GitHub Releases 获取最新版本信息。
    /// </summary>
    internal class GitHubUpdateSource : IUpdateSource
    {
        private const string Owner = "Lrht-llw";
        private const string Repo = "KfuPet";
        private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <inheritdoc />
        public async Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            // GitHub API 要求携带 User-Agent，否则返回 403
            request.Headers.UserAgent.ParseAdd("KfuPet-Update-Checker");

            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string releasePageUrl = root.TryGetProperty("html_url", out var htmlUrlElement)
                ? htmlUrlElement.GetString() ?? string.Empty
                : string.Empty;

            string releaseNotes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;

            return new ReleaseInfo
            {
                Version = tagElement.GetString() ?? string.Empty,
                ReleasePageUrl = releasePageUrl,
                ReleaseNotes = releaseNotes
            };
        }
    }
}
