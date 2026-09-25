using System.Reflection;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 检查更新：依次尝试多个更新源（GitHub → Gitee 国内镜像）做容错，
    /// 比较当前程序集版本与远端版本，返回结果。
    /// </summary>
    internal class UpdateService
    {
        // 容错顺序：GitHub 优先，失败或无结果时回退到 Gitee 国内镜像。
        private readonly IUpdateSource[] _sources =
        {
            new GitHubUpdateSource(),
            new GiteeUpdateSource()
        };

        private readonly Version _currentVersion;

        public UpdateService()
        {
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        }

        /// <summary>当前程序集版本。</summary>
        public Version CurrentVersion => _currentVersion;

        /// <summary>
        /// 获取“当前版本”对应的发布信息（关于页“关于此版本”使用）。
        /// 所有更新源均不可用或该版本没有发布信息时返回 null。
        /// </summary>
        public async Task<ReleaseInfo?> GetCurrentReleaseAsync()
        {
            foreach (var source in _sources)
            {
                try
                {
                    var release = await source.GetReleaseByVersionAsync(_currentVersion);
                    if (release != null)
                    {
                        return release;
                    }
                }
                catch
                {
                    continue; // 当前源失败，尝试下一个源
                }
            }

            return null;
        }

        /// <summary>
        /// 检查是否有新版本。所有更新源均不可用或无法判断时返回 null。
        /// </summary>
        public async Task<UpdateCheckResult?> CheckAsync()
        {
            foreach (var source in _sources)
            {
                ReleaseInfo? release;
                try
                {
                    release = await source.GetLatestReleaseAsync();
                }
                catch
                {
                    continue; // 当前源失败，尝试下一个源
                }

                if (release == null || !TryParseVersion(release.Version, out var latestVersion))
                {
                    continue;
                }

                return new UpdateCheckResult
                {
                    CurrentVersion = _currentVersion,
                    LatestVersion = latestVersion,
                    ReleaseNotes = release.ReleaseNotes,
                    PublishedAt = release.PublishedAt,
                    IsUpdateAvailable = latestVersion > _currentVersion
                };
            }

            return null;
        }

        /// <summary>
        /// 解析远端版本号，容忍 "v0.0.7" 前缀。
        /// </summary>
        private static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return Version.TryParse(normalized, out version!);
        }
    }
}
