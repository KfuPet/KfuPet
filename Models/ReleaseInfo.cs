namespace KfuPet.Models
{
    /// <summary>
    /// 远端发布信息，由更新源（GitHub / 自建服务器）返回。
    /// </summary>
    public class ReleaseInfo
    {
        /// <summary>远端版本号（形如 "0.0.7" 或 "v0.0.7"）。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>发布页地址。</summary>
        public string ReleasePageUrl { get; set; } = string.Empty;

        /// <summary>更新说明。</summary>
        public string ReleaseNotes { get; set; } = string.Empty;
    }
}
