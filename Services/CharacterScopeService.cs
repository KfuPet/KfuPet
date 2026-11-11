namespace KfuPet.Services
{
    /// <summary>角色数据作用域：短期记忆、归档记忆与情绪状态按角色包文件夹隔离，
    /// 长期记忆与 token 用量保持全局共享。作用域 key 使用角色包文件夹名（目录天然唯一），
    /// 没有可用角色包时使用默认作用域。</summary>
    internal static class CharacterScopeService
    {
        /// <summary>没有可用角色包时使用的默认作用域。</summary>
        public const string DefaultScopeKey = "_default";

        /// <summary>作用域内的短期记忆文件名。</summary>
        public const string ShortMemoryFileName = "ShortMemory.json";

        /// <summary>作用域内的归档记忆文件名。</summary>
        public const string ArchiveMemoryFileName = "ArchiveMemory.json";

        /// <summary>作用域内的情绪状态文件名。</summary>
        public const string EmotionFileName = "Emotion.json";

        /// <summary>角色作用域目录名（Memory 目录下）。</summary>
        private const string ScopedDirectoryName = "Characters";

        /// <summary>旧版本全局情绪状态文件名（位于 Config 目录）。</summary>
        private const string LegacyEmotionFileName = "emotion.json";

        /// <summary>解析当前生效角色包的作用域 key（文件夹名）；没有可用角色包时返回默认作用域。</summary>
        public static string ResolveActiveScopeKey()
        {
            var packageDir = CharacterCatalogService.ResolveActivePackageDirectory();
            return packageDir == null ? DefaultScopeKey : NormalizeScopeKey(Path.GetFileName(packageDir));
        }

        /// <summary>角色作用域的根目录：Memory\Characters。</summary>
        public static string GetScopedRootDirectory() =>
            Path.Combine(AppPaths.MemoryDirectory, ScopedDirectoryName);

        /// <summary>某个作用域的目录：Memory\Characters\&lt;角色文件夹&gt;。</summary>
        public static string GetScopeDirectory(string? scopeKey) =>
            Path.Combine(GetScopedRootDirectory(), NormalizeScopeKey(scopeKey));

        /// <summary>某个作用域的情绪状态文件路径。</summary>
        public static string GetEmotionFilePath(string? scopeKey) =>
            Path.Combine(GetScopeDirectory(scopeKey), EmotionFileName);

        /// <summary>
        /// 旧版本把记忆与情绪写在全局路径。升级后首次启动时，把旧文件迁入当前角色作用域：
        /// 短期/归档记忆与情绪归当前角色，长期记忆继续留在全局路径。
        /// 目标文件已存在时保留原文件，保证幂等；迁移失败时退化为复制，不丢数据。
        /// </summary>
        public static void MigrateLegacyFilesIfNeeded()
        {
            var packageDir = CharacterCatalogService.ResolveActivePackageDirectory();
            if (packageDir == null)
            {
                Log.Debug("[角色] 未找到可用角色包，跳过旧记忆迁移");
                return;
            }

            var scopeDir = GetScopeDirectory(Path.GetFileName(packageDir));
            try
            {
                Directory.CreateDirectory(scopeDir);
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色作用域目录创建失败（{scopeDir}）：{ex.Message}");
                return;
            }

            MoveLegacyFile(
                Path.Combine(AppPaths.MemoryDirectory, ShortMemoryFileName),
                Path.Combine(scopeDir, ShortMemoryFileName));
            MoveLegacyFile(
                Path.Combine(AppPaths.MemoryDirectory, ArchiveMemoryFileName),
                Path.Combine(scopeDir, ArchiveMemoryFileName));
            MoveLegacyFile(
                Path.Combine(AppPaths.ConfigDirectory, LegacyEmotionFileName),
                Path.Combine(scopeDir, EmotionFileName));
        }

        /// <summary>删除某个角色的全部作用域数据（删除角色包时调用）。</summary>
        public static void DeleteScope(string? scopeKey)
        {
            var scopeDir = GetScopeDirectory(scopeKey);
            try
            {
                if (!Directory.Exists(scopeDir)) return;

                Directory.Delete(scopeDir, recursive: true);
                Log.Info($"[角色] 已删除角色作用域数据：{scopeDir}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色作用域数据删除失败（{scopeDir}）：{ex.Message}");
            }
        }

        /// <summary>删除所有角色的短期与归档记忆文件（清理“记忆”时调用）。</summary>
        public static void DeleteAllScopedMemoryFiles()
        {
            foreach (var scopeDir in SafeGetScopeDirectories())
            {
                DeleteFileIfExists(Path.Combine(scopeDir, ShortMemoryFileName));
                DeleteFileIfExists(Path.Combine(scopeDir, ArchiveMemoryFileName));
            }

            // 迁移失败可能残留的旧版全局文件也一并清理
            DeleteFileIfExists(Path.Combine(AppPaths.MemoryDirectory, ShortMemoryFileName));
            DeleteFileIfExists(Path.Combine(AppPaths.MemoryDirectory, ArchiveMemoryFileName));
        }

        /// <summary>删除所有角色的情绪状态文件（清理“软件配置”时调用）。</summary>
        public static void DeleteAllEmotionFiles()
        {
            foreach (var scopeDir in SafeGetScopeDirectories())
            {
                DeleteFileIfExists(Path.Combine(scopeDir, EmotionFileName));
            }

            // 迁移失败可能残留的旧版全局情绪文件也一并清理
            DeleteFileIfExists(Path.Combine(AppPaths.ConfigDirectory, LegacyEmotionFileName));
        }

        /// <summary>把全局旧文件迁移到角色作用域；目标已存在时保留原文件。</summary>
        private static void MoveLegacyFile(string source, string target)
        {
            try
            {
                if (!File.Exists(source)) return;

                if (File.Exists(target))
                {
                    Log.Info($"[角色] 旧数据目标已存在，保留原文件不迁移：{source}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target);
                Log.Info($"[角色] 旧数据已迁移到角色作用域：{Path.GetFileName(source)} → {target}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 旧数据迁移失败（{source}）：{ex.Message}");
                TryCopyAsFallback(source, target);
            }
        }

        /// <summary>移动失败时退化为复制，原文件保留，避免升级过程丢数据。</summary>
        private static void TryCopyAsFallback(string source, string target)
        {
            try
            {
                if (!File.Exists(source) || File.Exists(target)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
                Log.Warning($"[角色] 旧数据已复制到角色作用域（原文件保留）：{target}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 旧数据复制也失败（{target}）：{ex.Message}");
            }
        }

        /// <summary>列出全部角色作用域目录；目录缺失或无权限时返回空集合。</summary>
        private static IEnumerable<string> SafeGetScopeDirectories()
        {
            try
            {
                var root = GetScopedRootDirectory();
                return Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色作用域目录读取失败：{ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static void DeleteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 作用域文件删除失败（{path}）：{ex.Message}");
            }
        }

        /// <summary>规范化作用域 key：去空白、替换非法文件名字符；空值回退默认作用域。</summary>
        private static string NormalizeScopeKey(string? scopeKey)
        {
            var key = scopeKey?.Trim();
            if (string.IsNullOrEmpty(key)) return DefaultScopeKey;

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(invalid, '_');
            }

            key = key.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(key) || key == "." || key == ".." ? DefaultScopeKey : key;
        }
    }
}
