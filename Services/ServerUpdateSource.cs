using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 自建服务器更新源（空壳占位）。
    /// 作为 GitHub 源不可用时的容错回退，具体实现说明见 docs/update-server-guide.md。
    /// </summary>
    internal class ServerUpdateSource : IUpdateSource
    {
        /// <inheritdoc />
        public Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            // 空壳实现：尚未接入服务器，直接返回 null，由 UpdateService 跳过该源。
            return Task.FromResult<ReleaseInfo?>(null);
        }
    }
}
