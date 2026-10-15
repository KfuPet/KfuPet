namespace KfuPet.Models
{
    /// <summary>
    /// 白名单里的一个允许打开的应用：显示名称 + 可执行文件完整路径。
    /// 名称供模型调用时引用；路径永远来自用户在设置中挑选的 .exe，模型无法提供路径。
    /// </summary>
    public sealed class AllowedAppEntry
    {
        /// <summary>应用显示名称，模型按此名称请求打开。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>可执行文件完整路径（由用户在设置中挑选）。</summary>
        public string ExecutablePath { get; set; } = string.Empty;
    }
}
