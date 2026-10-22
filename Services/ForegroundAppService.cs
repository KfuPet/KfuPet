using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KfuPet.Services
{
    /// <summary>
    /// 前台应用检测：只读取当前前台窗口所属应用的显示名称，供主动搭话补充语境。
    /// 不截图、不读取窗口内容；任何失败都静默返回 null，不影响主流程。
    /// </summary>
    internal static class ForegroundAppService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// 当前前台应用的显示名称（优先文件描述，取不到时退回进程名）。
        /// 前台是桌宠自身或读取失败时返回 null。
        /// </summary>
        public static string? TryGetForegroundAppName()
        {
            try
            {
                var window = GetForegroundWindow();
                if (window == IntPtr.Zero)
                {
                    return null;
                }

                GetWindowThreadProcessId(window, out var processId);
                if (processId == 0 || processId == (uint)Environment.ProcessId)
                {
                    return null;
                }

                using var process = Process.GetProcessById((int)processId);
                var name = GetDisplayName(process);
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch (Exception ex)
            {
                // 前台进程可能受权限保护或刚好退出，读取失败时本轮不注入语境
                Log.Debug($"[前台应用] 读取失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>优先用文件描述（如“Visual Studio”），取不到时退回进程名（如“devenv”）。</summary>
        private static string? GetDisplayName(Process process)
        {
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description))
                {
                    return description;
                }
            }
            catch
            {
                // 以更高权限运行的进程不允许读取主模块信息，退回进程名
            }

            return process.ProcessName;
        }
    }
}
