namespace KfuPet.Models
{
    /// <summary>
    /// 一次更新检查的结果。
    /// </summary>
    public class UpdateCheckResult
    {
        /// <summary>当前程序集版本。</summary>
        public Version CurrentVersion { get; set; } = new(0, 0, 0);

        /// <summary>远端最新版本。</summary>
        public Version LatestVersion { get; set; } = new(0, 0, 0);

        /// <summary>是否存在比当前更新的版本。</summary>
        public bool IsUpdateAvailable { get; set; }

        /// <summary>发布页地址。</summary>
        public string ReleasePageUrl { get; set; } = string.Empty;

        /// <summary>更新说明。</summary>
        public string ReleaseNotes { get; set; } = string.Empty;

        /// <summary>发布时间（UTC），未知时为 null。</summary>
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
