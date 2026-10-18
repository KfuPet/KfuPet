namespace KfuPet.Services
{
    /// <summary>自定义缓存目录的校验与迁移服务：校验用户选择的新目录是否可用；迁移按“先复制数据、再写注册表、最后删旧数据”执行，任一步失败都不丢数据。
    /// 迁移成功后需重启软件——各服务在启动时锁定路径，重启才会用上新目录。</summary>
    internal static class CachePathService
    {
        /// <summary>缓存根目录下需要随迁移一起搬走的子目录（与存储管理页的统计口径一致）。</summary>
        private static readonly string[] DataSubDirectories = { "Config", "Memory", "Logs" };

        /// <summary>
        /// 校验新缓存目录：必须是绝对路径，不能等于或包含当前缓存目录，不能是磁盘根目录，
        /// 不能放在软件安装目录内，并且可创建、可写入。失败时通过 <paramref name="error"/> 给出原因。
        /// </summary>
        public static bool TryValidate(string newRoot, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(newRoot))
            {
                error = "未选择目录";
                return false;
            }

            string normalized;
            try
            {
                if (!Path.IsPathRooted(newRoot))
                {
                    error = "请选择完整的绝对路径";
                    return false;
                }

                normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newRoot));
            }
            catch
            {
                error = "目录路径无效";
                return false;
            }

            var currentRoot = Path.TrimEndingDirectorySeparator(AppPaths.CacheRoot);

            if (string.Equals(normalized, currentRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "该目录已是当前缓存目录，无需更改";
                return false;
            }

            var root = Path.GetPathRoot(normalized);
            if (root != null &&
                string.Equals(Path.TrimEndingDirectorySeparator(root), normalized, StringComparison.OrdinalIgnoreCase))
            {
                error = "请使用磁盘下的文件夹，而不是磁盘根目录";
                return false;
            }

            var installDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));
            if (IsSameOrInside(normalized, installDirectory))
            {
                error = "不能把缓存目录放在软件安装目录内";
                return false;
            }

            if (IsSameOrInside(normalized, currentRoot) || IsSameOrInside(currentRoot, normalized))
            {
                error = "不能选择当前缓存目录本身或它的上级目录，请换一个位置";
                return false;
            }

            try
            {
                Directory.CreateDirectory(normalized);
                var probePath = Path.Combine(normalized, $".kfupet-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
            }
            catch
            {
                error = "该目录不可写入，请换一个位置";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 迁移缓存目录：把 Config / Memory / Logs 复制到新目录，成功后写入注册表并尽力删除旧数据。
        /// 单个文件被占用等复制失败只记日志、不中断；返回值表示是否迁移成功。
        /// </summary>
        public static bool Migrate(string newRoot, out string message)
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newRoot));
            var copiedFiles = 0;
            var failedFiles = 0;

            try
            {
                foreach (var sub in DataSubDirectories)
                {
                    var source = Path.Combine(AppPaths.CacheRoot, sub);
                    if (!Directory.Exists(source)) continue;

                    CopyDirectory(source, Path.Combine(fullPath, sub), ref copiedFiles, ref failedFiles);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[缓存] 迁移缓存目录失败，仍使用原目录：{ex}");
                message = "缓存数据迁移失败，仍使用原目录";
                return false;
            }

            if (!AppPaths.SetCacheRoot(fullPath))
            {
                message = "写入缓存路径设置失败，仍使用原目录";
                return false;
            }

            // 数据已复制且注册表已指向新目录，旧目录里的数据尽力删除；
            // 正在写入的文件（如当前日志）删不掉不影响迁移结果
            foreach (var sub in DataSubDirectories)
            {
                var oldDirectory = Path.Combine(AppPaths.CacheRoot, sub);
                try
                {
                    if (Directory.Exists(oldDirectory))
                    {
                        Directory.Delete(oldDirectory, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[缓存] 旧目录 {sub} 未能完全删除（可能被占用），不影响新目录使用：{ex.Message}");
                }
            }

            Log.Info($"[缓存] 缓存目录已迁移到 {fullPath}：复制 {copiedFiles} 个文件" +
                     (failedFiles > 0 ? $"，{failedFiles} 个文件被占用未能复制" : string.Empty));
            message = failedFiles > 0 ? "缓存已迁移到新目录（个别被占用的文件未随迁移）" : "缓存已迁移到新目录";
            return true;
        }

        /// <summary>递归复制目录：覆盖同名文件；单个文件复制失败（被占用等）只计数并继续。</summary>
        private static void CopyDirectory(string source, string target, ref int copiedFiles, ref int failedFiles)
        {
            Directory.CreateDirectory(target);

            foreach (var file in Directory.EnumerateFiles(source))
            {
                try
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                    copiedFiles++;
                }
                catch
                {
                    failedFiles++;
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), ref copiedFiles, ref failedFiles);
            }
        }

        /// <summary>判断 <paramref name="path"/> 是否就是 <paramref name="parent"/>，或位于其内部。</summary>
        private static bool IsSameOrInside(string path, string parent)
        {
            if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = parent.EndsWith(Path.DirectorySeparatorChar)
                ? parent
                : parent + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}