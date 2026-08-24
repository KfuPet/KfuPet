using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 更新源接口：获取远端最新发布信息。找不到或失败时返回 null（由上层做容错）。
    /// </summary>
    internal interface IUpdateSource
    {
        /// <summary>获取远端最新发布信息，无法获取时返回 null。</summary>
        Task<ReleaseInfo?> GetLatestReleaseAsync();
    }
}
