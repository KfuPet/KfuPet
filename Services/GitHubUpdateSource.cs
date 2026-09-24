using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 从 GitHub Releases 获取发布信息（最新版本 / 指定版本）。
    /// </summary>
    internal class GitHubUpdateSource : IUpdateSource
    {
        private const string Owner = "KfuPet";
        private const string Repo = "KfuPet";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases";

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <inheritdoc />
        public Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            return FetchReleaseAsync($"{ReleasesApiUrl}/latest");
        }

        /// <inheritdoc />
        public Task<ReleaseInfo?> GetReleaseByVersionAsync(Version version)
        {
            // 本仓库的 Release 一律以 “v版本号” 作为 tag
            return FetchReleaseAsync($"{ReleasesApiUrl}/tags/v{version.ToString(3)}");
        }

        /// <summary>
        /// 请求指定 API 地址并解析发布信息；响应不成功（例如该版本没有对应 Release 的 404）时返回 null。
        /// </summary>
        private static async Task<ReleaseInfo?> FetchReleaseAsync(string apiUrl)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            // GitHub API 要求携带 User-Agent，否则返回 403
            request.Headers.UserAgent.ParseAdd("KfuPet-Update-Checker");

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string releaseNotes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var publishedAtElement) &&
                publishedAtElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(publishedAtElement.GetString(), out var parsedPublishedAt))
            {
                publishedAt = parsedPublishedAt;
            }

            return new ReleaseInfo
            {
                Version = tagElement.GetString() ?? string.Empty,
                ReleaseNotes = releaseNotes,
                PublishedAt = publishedAt
            };
        }
    }
}
