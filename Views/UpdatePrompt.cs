using System.Windows;
using KfuPet.Models;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 更新提示的统一入口：把“检查结果 → 更新弹窗 → 拉起更新程序”这段流程集中在一处，
    /// 供设置窗口的“检查更新”与启动时的静默检查共用。
    /// </summary>
    internal static class UpdatePrompt
    {
        /// <summary>
        /// 显示更新结果弹窗。用户确认且有新版本时拉起更新程序，拉起成功才退出桌宠。
        /// </summary>
        /// <param name="owner">弹窗属主窗口，可为 null。</param>
        /// <param name="result">检查更新结果。</param>
        public static void Show(Window? owner, UpdateCheckResult result)
        {
            var dialog = new UpdateDialog(
                result.IsUpdateAvailable,
                result.CurrentVersion.ToString(3),
                result.LatestVersion.ToString(3),
                result.ReleaseNotes);

            if (owner != null)
            {
                dialog.Owner = owner;
            }

            dialog.UpdateConfirmed += (s, e) =>
            {
                if (!UpdaterLauncher.TryLaunchUpdate(out string error))
                {
                    // 拉起失败（未安装、缺更新程序、用户取消 UAC）→ 留在桌宠里提示，不要退出。
                    MessageBox.Show(error, "KfuPet", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 拉起成功才退出：更新程序在等这个进程结束，退不掉它就换不了文件。
                // 走正常关闭（而不是 Environment.Exit），让 App.OnExit 里的保存与托盘释放照常执行。
                Application.Current.Shutdown();
            };

            dialog.ShowDialog();
        }
    }
}
