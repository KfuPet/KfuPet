using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KfuPet.Services
{
    /// <summary>任务栏上一个正在运行的应用：显示名称，以及是否为主人当前正在使用的前台应用。</summary>
    internal readonly record struct RunningAppInfo(string Name, bool IsForeground);

    /// <summary>
    /// 应用状态检测：读取任务栏上正在运行的应用（Alt+Tab 口径：可见或最小化的顶层窗口），
    /// 供主动搭话语境与“查看正在使用的应用”工具使用。
    /// 只读取应用名称与前台应用的窗口标题，不读取窗口内容；任何失败都静默返回空结果，不影响主流程。
    /// </summary>
    internal static class ForegroundAppService
    {
        /// <summary>单次最多返回的应用数量，避免窗口很多时语境与工具结果过长。</summary>
        private const int MAX_RUNNING_APPS = 15;

        /// <summary>前台应用标签使用窗口标题的最大长度，超出截断，避免语境过长。</summary>
        private const int MAX_FOREGROUND_TITLE_LENGTH = 40;

        /// <summary>允许用窗口标题代替应用名的进程名（忽略大小写）：
        /// 这些应用的任务栏标题能反映主人正在看/编辑的内容，且隐私风险相对可控。</summary>
        private static readonly HashSet<string> TitleAppProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // 浏览器
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "360chrome", "360se", "qqbrowser",
            // IDE 与编辑器
            "code", "devenv", "idea64", "pycharm64", "webstorm64", "clion64", "rider64", "goland64", "studio64",
            "notepad++", "sublime_text",
            // 办公
            "winword", "excel", "powerpnt", "onenote", "wps", "et", "wpp",
            // 终端
            "windowsterminal", "pwsh", "powershell", "cmd",
            // 笔记
            "obsidian", "typora",
            // 设计 / 3D
            "photoshop", "illustrator", "figma", "blender", "unity", "unrealeditor"
        };

        private const int GWL_EXSTYLE = -20;

        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const int DWMWA_CLOAKED = 14;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// 任务栏上正在运行的应用（按显示名去重），并标记当前前台应用。
        /// 前台是桌宠自身、窗口受保护或读取失败时对应窗口会被跳过；没有可用结果时返回空列表。
        /// </summary>
        public static IReadOnlyList<RunningAppInfo> TryGetRunningApps()
        {
            try
            {
                var apps = new List<RunningAppInfo>();
                var appIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var foregroundWindow = GetForegroundWindow();

                EnumWindowsProc callback = (window, _) =>
                {
                    if (apps.Count >= MAX_RUNNING_APPS)
                    {
                        return false;
                    }

                    TryAddApp(window, foregroundWindow, apps, appIndexes);
                    return true;
                };

                EnumWindows(callback, IntPtr.Zero);
                return apps;
            }
            catch (Exception ex)
            {
                // 枚举窗口依赖系统接口，失败时本轮不注入应用语境
                Log.Debug($"[应用语境] 读取运行应用失败：{ex.Message}");
                return Array.Empty<RunningAppInfo>();
            }
        }

        /// <summary>检查单个顶层窗口，符合任务栏应用特征时加入结果。</summary>
        private static void TryAddApp(
            IntPtr window,
            IntPtr foregroundWindow,
            List<RunningAppInfo> apps,
            Dictionary<string, int> appIndexes)
        {
            if (!IsWindowVisible(window))
            {
                return;
            }

            var title = GetWindowTitle(window);
            if (string.IsNullOrEmpty(title) ||
                string.Equals(title, "Program Manager", StringComparison.OrdinalIgnoreCase))
            {
                // 无标题窗口多为系统壳层窗口；“Program Manager”是桌面本身
                return;
            }

            // 工具窗口（浮动面板、提示框等）不会出现在任务栏上；被系统隐藏的窗口（如其他虚拟桌面）也跳过
            if ((GetWindowLong(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0 || IsCloaked(window))
            {
                return;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                return;
            }

            Process process;
            try
            {
                process = Process.GetProcessById((int)processId);
            }
            catch (Exception ex)
            {
                // 窗口所属进程可能刚好退出或受权限保护
                Log.Debug($"[应用语境] 读取进程 {processId} 失败：{ex.Message}");
                return;
            }

            using (process)
            {
                var appName = GetDisplayName(process, title);
                if (string.IsNullOrWhiteSpace(appName))
                {
                    return;
                }

                var isForeground = window == foregroundWindow;
                var label = isForeground ? GetForegroundLabel(process, title, appName) : appName;

                // 同一应用只保留一项；若后来才遇到它的前台窗口，用标题替换原先的标签
                if (appIndexes.TryGetValue(appName, out var index))
                {
                    if (isForeground)
                    {
                        apps[index] = new RunningAppInfo(label, true);
                    }

                    return;
                }

                appIndexes[appName] = apps.Count;
                apps.Add(new RunningAppInfo(label, isForeground));
            }
        }

        /// <summary>前台应用的显示标签：白名单内的应用改用窗口标题（过长截断），让语境能反映正在看/编辑的内容；
        /// 其余应用仍用应用名，避免把其他窗口标题也带出去。</summary>
        private static string GetForegroundLabel(Process process, string title, string appName)
        {
            try
            {
                if (!TitleAppProcessNames.Contains(process.ProcessName) || string.IsNullOrWhiteSpace(title))
                {
                    return appName;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[应用语境] 读取前台进程名失败：{ex.Message}");
                return appName;
            }

            return title.Length > MAX_FOREGROUND_TITLE_LENGTH
                ? title[..MAX_FOREGROUND_TITLE_LENGTH] + "…"
                : title;
        }

        /// <summary>窗口标题；没有标题时返回 null。</summary>
        private static string? GetWindowTitle(IntPtr window)
        {
            var length = GetWindowTextLength(window);
            if (length <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length + 1);
            GetWindowText(window, builder, builder.Capacity);
            var title = builder.ToString().Trim();
            return string.IsNullOrEmpty(title) ? null : title;
        }

        /// <summary>
        /// 优先用文件描述（如“Visual Studio”），取不到时退回进程名（如“devenv”）；
        /// UWP 窗口的宿主进程固定是 ApplicationFrameHost，改用窗口标题作为应用名。
        /// </summary>
        private static string? GetDisplayName(Process process, string title)
        {
            try
            {
                var processName = process.ProcessName;
                if (!string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    var description = process.MainModule?.FileVersionInfo.FileDescription?.Trim();
                    return string.IsNullOrEmpty(description) ? processName : description;
                }
            }
            catch
            {
                // 以更高权限运行的进程不允许读取主模块信息，退回窗口标题或进程名
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            try
            {
                return process.ProcessName;
            }
            catch (Exception ex)
            {
                Log.Debug($"[应用语境] 读取进程名失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>窗口是否被系统隐藏（例如位于其他虚拟桌面）；查询失败按未隐藏处理。</summary>
        private static bool IsCloaked(IntPtr window)
        {
            try
            {
                return DwmGetWindowAttribute(window, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
