namespace KfuPet.Services
{
    /// <summary>单个模型包（Characters 下的角色包文件夹）的占用信息。</summary>
    internal class ModelPackageUsage
    {
        /// <summary>模型包名称（文件夹名）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>模型包占用的字节数。</summary>
        public long Bytes { get; init; }
    }

    /// <summary>
    /// 存储占用统计结果：软件本体（安装位置）、缓存目录与模型包目录的占用大小。
    /// </summary>
    internal class StorageUsageInfo
    {
        /// <summary>软件本体目录（程序输出 / 安装目录）占用的字节数。</summary>
        public long InstallBytes { get; init; }

        /// <summary>缓存目录（%AppData%\KfuPet）占用的字节数。</summary>
        public long CacheBytes { get; init; }

        /// <summary>模型包目录（Characters）；未找到时为空。</summary>
        public string ModelsPath { get; init; } = string.Empty;

        /// <summary>全部模型包占用的总字节数。</summary>
        public long ModelsBytes { get; init; }

        /// <summary>各模型包的占用（按大小降序排列）。</summary>
        public IReadOnlyList<ModelPackageUsage> Packages { get; init; } = Array.Empty<ModelPackageUsage>();
    }

    /// <summary>
    /// 存储占用统计服务：统计软件本体、缓存目录与模型包目录的占用大小。
    /// 目录遍历在后台线程完成；无权限或读取失败的条目自动跳过，不影响整体统计。
    /// </summary>
    internal static class StorageUsageService
    {
        private const long Kilobyte = 1024;
        private const long Megabyte = Kilobyte * 1024;
        private const long Gigabyte = Megabyte * 1024;
        private const long Terabyte = Gigabyte * 1024;

        /// <summary>在后台线程统计一次存储占用。</summary>
        public static Task<StorageUsageInfo> LoadUsageAsync()
        {
            return Task.Run(() =>
            {
                var installPath = AppDomain.CurrentDomain.BaseDirectory;
                var cachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet");
                var modelsPath = PromptService.FindCharactersDirectory();

                var packages = new List<ModelPackageUsage>();
                if (modelsPath != null)
                {
                    foreach (var packageDir in SafeGetDirectories(modelsPath))
                    {
                        packages.Add(new ModelPackageUsage
                        {
                            Name = Path.GetFileName(packageDir),
                            Bytes = GetDirectorySize(packageDir)
                        });
                    }

                    // 大小降序排列：页面据此展示占用最大的几个模型包
                    packages.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                }

                var usage = new StorageUsageInfo
                {
                    InstallBytes = GetDirectorySize(installPath),
                    CacheBytes = GetDirectorySize(cachePath),
                    ModelsPath = modelsPath ?? string.Empty,
                    ModelsBytes = packages.Sum(p => p.Bytes),
                    Packages = packages
                };

                Log.Info($"[存储] 占用统计完成：本体 {FormatBytes(usage.InstallBytes)}，" +
                         $"缓存 {FormatBytes(usage.CacheBytes)}，" +
                         $"模型包 {FormatBytes(usage.ModelsBytes)}（{packages.Count} 个）");
                return usage;
            });
        }

        /// <summary>递归统计目录总大小；无权限等读取失败的条目直接跳过，不中断整体统计。</summary>
        private static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;

            long totalBytes = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        try
                        {
                            totalBytes += new FileInfo(file).Length;
                        }
                        catch
                        {
                            // 文件恰好被占用或刚被删除时跳过这一个文件
                        }
                    }

                    foreach (var subDir in Directory.EnumerateDirectories(current))
                    {
                        pending.Push(subDir);
                    }
                }
                catch
                {
                    // 目录无访问权限时跳过
                }
            }

            return totalBytes;
        }

        /// <summary>列出子目录；目录缺失或无权限时返回空集合。</summary>
        private static IReadOnlyList<string> SafeGetDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>把字节数格式化成便于阅读的文本（B / KB / MB / GB / TB）。</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes >= Terabyte) return $"{bytes / (double)Terabyte:0.##} TB";
            if (bytes >= Gigabyte) return $"{bytes / (double)Gigabyte:0.##} GB";
            if (bytes >= Megabyte) return $"{bytes / (double)Megabyte:0.#} MB";
            if (bytes >= Kilobyte) return $"{bytes / (double)Kilobyte:0.#} KB";
            return $"{bytes} B";
        }
    }
}