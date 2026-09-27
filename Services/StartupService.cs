using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 开机自启动开关，通过当前用户注册表 Run 项实现（无需管理员权限）。
    /// 状态以注册表为准，外部改动后重新打开设置界面也能如实显示。
    /// </summary>
    internal class StartupService
    {
        /// <summary>
        /// 开机自启动时追加的命令行参数，App 据此识别“开机拉起”并跳过启动动画。
        /// </summary>
        public const string AutoStartArgument = "--autostart";

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string AppName = "KfuPet";

        /// <summary>
        /// 开机自启动是否已开启。
        /// </summary>
        public bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) is string value && value.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Error($"[自启] 读取开机自启动状态失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 开启 / 关闭开机自启动，返回是否设置成功。
        /// </summary>
        public bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null)
                {
                    return false;
                }

                if (enabled)
                {
                    var commandLine = BuildCommandLine();
                    if (commandLine == null)
                    {
                        return false;
                    }

                    key.SetValue(AppName, commandLine);
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }

                Log.Info($"[自启] 开机自启动已{(enabled ? "开启" : "关闭")}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[自启] 设置开机自启动失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 开机自启动仍开启时，按当前程序路径与静默参数校正登记项：
        /// 兼容旧版本写入的不带参数的启动项，以及程序被移动后的路径变化。
        /// </summary>
        public void SyncRegistration()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (key?.GetValue(AppName) is not string currentValue || currentValue.Length == 0)
                {
                    return;
                }

                var commandLine = BuildCommandLine();
                if (commandLine == null || commandLine == currentValue)
                {
                    return;
                }

                if (SetEnabled(true))
                {
                    Log.Info("[自启] 启动项已按当前路径与静默参数校正");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[自启] 校正开机自启动登记项失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 生成启动项命令行：程序路径加引号（路径含空格时必需），后跟静默启动参数。
        /// 取不到进程路径时返回 null。
        /// </summary>
        private static string? BuildCommandLine()
        {
            var exePath = Environment.ProcessPath;
            return string.IsNullOrEmpty(exePath) ? null : $"\"{exePath}\" {AutoStartArgument}";
        }
    }
}