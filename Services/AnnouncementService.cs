using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 官方公告服务：读取公告仓库（KfuPet/.github）根目录的 README.md，
    /// 将其中的 HTML / Markdown 公告转换为弹窗内容；远端不可用时回退到本地缓存。
    /// 公告内容一旦变化（标题、日期或正文），标识随之变化，下一次启动会再次弹窗。
    /// </summary>
    internal class AnnouncementService
    {
        /// <summary>公告文件（GitHub 直连，更新最及时）。</summary>
        private const string GitHubRawUrl =
            "https://raw.githubusercontent.com/KfuPet/.github/main/README.md";

        /// <summary>公告文件（jsDelivr 国内镜像，GitHub 不可达时兜底）。</summary>
        private const string MirrorUrl =
            "https://cdn.jsdelivr.net/gh/KfuPet/.github@main/README.md";

        private const string UserAgent = "KfuPet-Announcement-Client";
        private const string CacheFileName = "announcement-cache.json";
        private const string DateLabel = "更新日期";

        /// <summary>匹配 &lt;a href="..."&gt;文字&lt;/a&gt;，转换为 Markdown 链接。</summary>
        private const string LinkPattern =
            @"<a\s[^>]*href\s*=\s*[""'](?<url>[^""']*)[""'][^>]*>(?<label>.*?)</a>";

        /// <summary>匹配“更新日期：10/25”一类的日期标注。</summary>
        private const string DatePattern =
            @"更新日期\s*[:：]\s*(?<date>[0-9]{1,4}\s*[/\-.]\s*[0-9]{1,2}(?:\s*[/\-.]\s*[0-9]{1,4})?)";

        /// <summary>公告缓存文件位置（Config/announcement-cache.json）。</summary>
        private static readonly string CacheFilePath = Path.Combine(AppPaths.ConfigDirectory, CacheFileName);

        // GitHub 直连在国内常被阻断，超时收得短一些，尽快回退到镜像
        private static readonly HttpClient GitHubClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private static readonly HttpClient MirrorClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly JsonSerializerOptions CacheSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        /// <summary>
        /// 获取最新公告：优先远端 README，全部源不可用时读取本地缓存。
        /// 远端明确读取到空内容时清空缓存并返回 null（表示当前没有公告）。
        /// </summary>
        public async Task<Announcement?> GetLatestAsync()
        {
            Log.Debug("[公告] 开始获取官方公告");

            var (success, content) = await TryFetchRemoteAsync();
            if (success)
            {
                var announcement = content == null ? null : ParseReadme(content);
                if (announcement != null)
                {
                    SaveCache(announcement);
                    Log.Info($"[公告] 已解析公告：{announcement.Title}（{announcement.Id}）");
                }
                else
                {
                    ClearCache();
                    Log.Debug("[公告] 公告内容为空或无法解析");
                }

                return announcement;
            }

            return LoadCache();
        }

        /// <summary>
        /// 依次尝试 GitHub 直连与 jsDelivr 镜像；任一源成功即返回。
        /// </summary>
        private static async Task<(bool Success, string? Content)> TryFetchRemoteAsync()
        {
            var result = await TryFetchAsync(GitHubClient, GitHubRawUrl, "GitHub");
            if (result.Success)
            {
                return result;
            }

            return await TryFetchAsync(MirrorClient, MirrorUrl, "jsDelivr 镜像");
        }

        /// <summary>请求单个公告源并返回 README 原文。</summary>
        private static async Task<(bool Success, string? Content)> TryFetchAsync(
            HttpClient client, string url, string sourceName)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(UserAgent);

                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"[公告] {sourceName} 返回 HTTP {(int)response.StatusCode}");
                    return (false, null);
                }

                var content = await response.Content.ReadAsStringAsync();
                Log.Info($"[公告] 已从 {sourceName} 获取公告文件");
                return (true, string.IsNullOrWhiteSpace(content) ? null : content);
            }
            catch (Exception ex)
            {
                Log.Warning($"[公告] {sourceName} 不可用：{ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// 把 README 转换为公告：第一段非空文字作为标题，去掉日期行，其余内容作为 Markdown 正文。
        /// 同时兼容 HTML 写法（当前 README）与纯 Markdown 写法。
        /// </summary>
        private static Announcement? ParseReadme(string readme)
        {
            var markdown = ConvertToMarkdown(readme);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            var lines = markdown.Split('\n');
            var title = string.Empty;
            var titleFound = false;
            var bodyLines = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();

                if (!titleFound)
                {
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    title = trimmed.TrimStart('#').Trim();
                    titleFound = true;
                    continue;
                }

                // 日期单独显示在标题下方，正文中不再重复
                if (trimmed.StartsWith(DateLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                bodyLines.Add(line);
            }

            if (title.Length == 0)
            {
                return null;
            }

            var body = Regex.Replace(string.Join("\n", bodyLines).Trim(), @"\n{3,}", "\n\n");

            return new Announcement
            {
                // 用公告原文计算标识：只要内容有改动，下次启动就会重新弹窗
                Id = ComputeId(readme),
                Date = ExtractDate(readme),
                Level = "info",
                Title = title,
                Body = body,
                Link = string.Empty
            };
        }

        /// <summary>把 README 中的 HTML 片段转换为 Markdown，保留链接与段落结构。</summary>
        private static string ConvertToMarkdown(string readme)
        {
            var text = Regex.Replace(
                readme,
                LinkPattern,
                ConvertLink,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</(?:p|div|h[1-6]|li|tr)>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);

            text = WebUtility.HtmlDecode(text);
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        }

        /// <summary>把 &lt;a&gt; 标签转换为 Markdown 链接。</summary>
        private static string ConvertLink(Match match)
        {
            var label = Regex.Replace(match.Groups["label"].Value, @"<[^>]+>", string.Empty).Trim();
            label = WebUtility.HtmlDecode(label);

            var url = match.Groups["url"].Value.Trim();
            if (label.Length == 0)
            {
                label = url;
            }

            return $"[{label}]({url})";
        }

        /// <summary>提取“更新日期：10/25”中的日期文本；没有日期时返回空串。</summary>
        private static string ExtractDate(string readme)
        {
            var match = Regex.Match(readme, DatePattern, RegexOptions.IgnoreCase);
            return match.Success
                ? Regex.Replace(match.Groups["date"].Value, @"\s+", string.Empty)
                : string.Empty;
        }

        /// <summary>用 README 原文计算公告标识，内容变化即得到新标识。</summary>
        private static string ComputeId(string readme)
        {
            var normalized = readme.Replace("\r\n", "\n").Trim();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "readme-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }

        /// <summary>把公告写入本地缓存，供远端不可用时兜底。</summary>
        private static void SaveCache(Announcement announcement)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                File.WriteAllText(
                    CacheFilePath,
                    JsonSerializer.Serialize(announcement, CacheSerializerOptions));
            }
            catch (Exception ex)
            {
                Log.Warning($"[公告] 写入公告缓存失败：{ex.Message}");
            }
        }

        /// <summary>读取本地公告缓存；不存在或损坏时返回 null。</summary>
        private static Announcement? LoadCache()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                {
                    Log.Debug("[公告] 所有公告源均不可用，且没有本地缓存");
                    return null;
                }

                var cached = JsonSerializer.Deserialize<Announcement>(
                    File.ReadAllText(CacheFilePath),
                    CacheSerializerOptions);

                if (cached == null ||
                    string.IsNullOrWhiteSpace(cached.Id) ||
                    string.IsNullOrWhiteSpace(cached.Title))
                {
                    Log.Warning("[公告] 本地公告缓存内容不完整");
                    return null;
                }

                Log.Info($"[公告] 所有公告源均不可用，使用本地缓存：{cached.Id}");
                return cached;
            }
            catch (Exception ex)
            {
                Log.Warning($"[公告] 读取公告缓存失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>远端明确没有公告时删除缓存，避免已撤下的旧公告继续弹出。</summary>
        private static void ClearCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    File.Delete(CacheFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[公告] 删除公告缓存失败：{ex.Message}");
            }
        }
    }
}
