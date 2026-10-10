namespace KfuPet.Services
{
    /// <summary>
    /// 缓存清理服务：负责删除可清理的本地文件（历史日志、软件配置文件）。
    /// 记忆的清空由 <see cref="MemorySystem"/> 负责，设置 / 模型 / 停用词的内存复位
    /// 由各服务自身的 ResetToDefaults 负责，调用方（设置窗口）按需组合。
    /// </summary>
    internal static class CacheCleanupService
    {
        /// <summary>软件配置目录：%AppData%\KfuPet\Config。</summary>
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Config");

        /// <summary>属于“软件配置”的文件清单。</summary>
        private static readonly string[] ConfigFileNames =
        {
            "settings.json",
            "models.json",
            "stopwords.json"
        };

        /// <summary>
        /// 删除日志目录下的历史日志，保留当前正在写入的那一份；
        /// <paramref name="keepFilePath"/> 为 null（未启用落盘）时全部删除。
        /// 返回实际删除的文件数量。
        /// </summary>
        public static int DeleteLogFiles(string? keepFilePath)
        {
            var deleted = 0;
            try
            {
                foreach (var file in Directory.GetFiles(LogFileWriter.LogDirectory, "KfuPet-*.log"))
                {
                    if (keepFilePath != null &&
                        string.Equals(Path.GetFullPath(file), Path.GetFullPath(keepFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch
                    {
                        // 文件被占用时跳过，留着下次清理
                    }
                }
            }
            catch
            {
                // 日志目录不存在或无权限：视为没有可清理的文件
            }

            Log.Info($"[清理] 历史日志已删除 {deleted} 份（正在写入的日志保留）");
            return deleted;
        }

        /// <summary>删除软件配置文件（通用设置、模型配置、停用词）；返回实际删除的文件数量。</summary>
        public static int DeleteConfigFiles()
        {
            var deleted = 0;
            foreach (var fileName in ConfigFileNames)
            {
                var path = Path.Combine(ConfigDirectory, fileName);
                try
                {
                    if (!File.Exists(path)) continue;

                    File.Delete(path);
                    deleted++;
                }
                catch
                {
                    // 单个文件删除失败不影响其他配置文件
                }
            }

            Log.Info($"[清理] 软件配置已删除 {deleted} 个文件");
            return deleted;
        }
    }
}