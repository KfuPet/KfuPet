using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 开机自启动开关，通过当前用户注册表 Run 项实现（无需管理员权限）。
    /// 状态以注册表为准，外部改动后重新打开设置界面也能如实显示。
    /// </summary>
    internal class StartupService
    {
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
                    var exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        return false;
                    }

                    // 路径带空格时需加引号，否则系统会拆成多个参数
                    key.SetValue(AppName, $"\"{exePath}\"");
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
    }
}