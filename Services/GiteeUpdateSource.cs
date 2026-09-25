using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 从 Gitee Releases 获取发布信息（最新版本 / 指定版本）。
    /// 作为 GitHub 源不可达时的国内镜像兜底，镜像仓库与 KfuPetUpdate 保持一致。
    /// Gitee 的 v5 接口读取公开仓库无需鉴权，字段与 GitHub 的差异是
    /// 发布时间叫 created_at；tag 约定与 GitHub 相同，都是 “v版本号”。
    /// </summary>
    internal class GiteeUpdateSource : IUpdateSource
    {
        private const string Owner = "lrht";
        private const string Repo = "kfu-pet";
        private const string ReleasesApiUrl = $"https://gitee.com/api/v5/repos/{Owner}/{Repo}/releases";

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
            // 与 GitHub 源同一套 tag 约定：v版本号
            return FetchReleaseAsync($"{ReleasesApiUrl}/tags/v{version.ToString(3)}");
        }

        /// <summary>
        /// 请求指定 API 地址并解析发布信息；响应不成功（例如该版本没有对应 Release 的 404）时返回 null。
        /// </summary>
        private static async Task<ReleaseInfo?> FetchReleaseAsync(string apiUrl)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            // 统一带上 User-Agent，避免被接口方当作异常客户端拦下
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
            if (root.TryGetProperty("created_at", out var createdAtElement) &&
                createdAtElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(createdAtElement.GetString(), out var parsedCreatedAt))
            {
                publishedAt = parsedCreatedAt;
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
