using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 应用本地数据目录解析：缓存根目录（软件配置、记忆与日志的父目录）支持用户自定义，
    /// 记录在当前用户注册表 HKCU\Software\KfuPet 的 CacheRoot 值里。
    /// 启动时读取一次，进程内固定不变——更改缓存目录后需要重启软件才会生效。
    /// </summary>
    internal static class AppPaths
    {
        /// <summary>注册表项：存放用户级路径设置。</summary>
        private const string RegistryKeyPath = @"Software\KfuPet";

        /// <summary>注册表值名：自定义缓存根目录（REG_SZ，绝对路径）。</summary>
        private const string CacheRootValueName = "CacheRoot";

        /// <summary>默认缓存根目录：%AppData%\KfuPet。</summary>
        public static string DefaultCacheRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet");

        /// <summary>当前缓存根目录：注册表里存在有效设置时用自定义路径，否则用默认目录。</summary>
        public static string CacheRoot { get; } = ResolveCacheRoot();

        /// <summary>软件配置目录（settings.json / models.json / stopwords.json）。</summary>
        public static string ConfigDirectory { get; } = Path.Combine(CacheRoot, "Config");

        /// <summary>记忆目录（短期 / 归档 / 长期记忆 JSON）。</summary>
        public static string MemoryDirectory { get; } = Path.Combine(CacheRoot, "Memory");

        /// <summary>日志目录（KfuPet-*.log）。</summary>
        public static string LogsDirectory { get; } = Path.Combine(CacheRoot, "Logs");

        /// <summary>
        /// 把缓存根目录写入注册表：等于默认目录时删除记录（恢复默认），否则写入自定义路径。
        /// 返回值表示是否写入成功。
        /// </summary>
        public static bool SetCacheRoot(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                if (key == null)
                {
                    return false;
                }

                if (string.Equals(fullPath, DefaultCacheRoot, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(CacheRootValueName, throwOnMissingValue: false);
                }
                else
                {
                    key.SetValue(CacheRootValueName, fullPath, RegistryValueKind.String);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[缓存] 写入自定义缓存路径失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取注册表里的自定义缓存路径；未设置、无效（非绝对路径或目录不存在）
        /// 或读取失败时回退默认目录。
        /// </summary>
        private static string ResolveCacheRoot()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key?.GetValue(CacheRootValueName) is string value &&
                    Path.IsPathRooted(value) &&
                    Directory.Exists(value))
                {
                    var fullPath = Path.GetFullPath(value);
                    Log.Info($"[缓存] 已启用自定义缓存目录：{fullPath}");
                    return fullPath;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[缓存] 读取自定义缓存路径失败，改用默认目录：{ex.Message}");
            }

            return DefaultCacheRoot;
        }
    }
}