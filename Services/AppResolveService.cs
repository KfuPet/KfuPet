using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 应用名称 → 可执行文件路径 的解析，仅在“完全访问”模式下使用。
    /// 依次尝试：注册表 App Paths → 开始菜单快捷方式（含按快捷方式目标名匹配）。
    /// 全程不新增第三方依赖：注册表用 Microsoft.Win32，快捷方式解析用系统自带的 WScript.Shell COM。
    /// 解析不到时返回 null，由调用方决定是否再交给 ShellExecute。
    /// </summary>
    internal static class AppResolveService
    {
        /// <summary>开始菜单第二遍（按快捷方式目标名匹配）最多解析的快捷方式数量，避免异常环境下过慢。</summary>
        private const int MaxShortcutResolve = 300;

        /// <summary>
        /// 把名称归一化：只保留字母数字与中日韩文字并转小写，用于宽容比较（忽略空格、符号、大小写差异）。
        /// </summary>
        private static string Normalize(string text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        /// <summary>把应用名称解析为可执行文件路径；解析不到返回 null。</summary>
        public static string? Resolve(string name)
        {
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return null;
            }

            // 名称本身就是一个存在的可执行文件时直接使用
            if (File.Exists(name))
            {
                return name;
            }

            return ResolveFromAppPaths(name) ?? ResolveFromStartMenu(name);
        }

        /// <summary>
        /// 注册表 App Paths：HKLM / HKCU 下（含 32 位 WOW6432Node 视图）的 {name}.exe 默认值。
        /// </summary>
        private static string? ResolveFromAppPaths(string name)
        {
            var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };

            foreach (var prefix in new[] { string.Empty, @"WOW6432Node\" })
            {
                var subPath = @"SOFTWARE\" + prefix + @"Microsoft\Windows\CurrentVersion\App Paths\" + exeName;
                foreach (var hive in hives)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(subPath);
                        if (key?.GetValue(null) is string path && path.Length > 0 && File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch
                    {
                        // 单个注册表项读取失败不影响其它尝试
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 扫描开始菜单（用户 + 公共）里的 .lnk：先按快捷方式文件名匹配，再按快捷方式目标文件名匹配。
        /// </summary>
        private static string? ResolveFromStartMenu(string name)
        {
            var target = Normalize(name);
            if (target.Length == 0)
            {
                return null;
            }

            var shortcuts = new List<string>();
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                     })
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        shortcuts.AddRange(Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories));
                    }
                }
                catch
                {
                    // 目录不可读时跳过
                }
            }

            // 第一遍：快捷方式文件名匹配（便宜，无需解析目标）
            foreach (var shortcut in shortcuts)
            {
                if (Normalize(Path.GetFileNameWithoutExtension(shortcut)) == target)
                {
                    var resolved = ResolveShortcutTarget(shortcut);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }

            // 第二遍：解析快捷方式目标文件名匹配（设数量上限，避免过慢）
            var resolvedCount = 0;
            foreach (var shortcut in shortcuts)
            {
                if (resolvedCount >= MaxShortcutResolve)
                {
                    break;
                }

                var resolved = ResolveShortcutTarget(shortcut);
                resolvedCount++;
                if (resolved != null && Normalize(Path.GetFileNameWithoutExtension(resolved)) == target)
                {
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>用 WScript.Shell 解析 .lnk 的目标；非可执行文件或解析失败时返回 null。</summary>
        private static string? ResolveShortcutTarget(string shortcutPath)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return null;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(shortcutPath);
                string path = link.TargetPath;

                return !string.IsNullOrWhiteSpace(path) &&
                       path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                       File.Exists(path)
                    ? path
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
