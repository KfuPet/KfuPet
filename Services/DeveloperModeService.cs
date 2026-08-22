using System.Diagnostics;

namespace KfuPet.Services
{
    /// <summary>
    /// 开发者模式开关状态，负责保存开关状态并检测开发者工具（KfuPet-Tool）是否在运行。
    /// </summary>
    internal class DeveloperModeService
    {
        private const string ToolProcessName = "KfuPet-Tool";

        /// <summary>
        /// 开发者模式是否启用。
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// 开关状态变化时触发。
        /// </summary>
        public event EventHandler? EnabledChanged;

        /// <summary>
        /// 设置开发者模式开关状态。
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 检查开发者工具（KfuPet-Tool）进程是否正在运行。
        /// </summary>
        public bool IsToolRunning()
        {
            try
            {
                return Process.GetProcessesByName(ToolProcessName).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
