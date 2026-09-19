using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 拉起常驻更新程序完成升级。升级过程由更新程序负责，本类只负责把它启动起来。
    /// </summary>
    internal static class UpdaterLauncher
    {
        /// <summary>注册表安装记录位置，与 KfuPetUpdate 的约定一致。</summary>
        private const string InstallKeyPath = @"Software\KfuPet";

        /// <summary>安装目录内常驻更新程序的文件名，由安装程序放置。</summary>
        private const string UpdaterFileName = "KfuPetUpdate.exe";

        /// <summary>
        /// 尝试拉起更新程序。返回 true 表示已经启动成功，
        /// 调用方此时应立即退出，把安装目录让给更新程序。
        /// </summary>
        public static bool TryLaunchUpdate(out string error)
        {
            error = string.Empty;

            string installDir = ReadInstallPath();
            if (string.IsNullOrWhiteSpace(installDir))
            {
                error = "未找到 KfuPet 的安装位置，无法自动更新。";
                return false;
            }

            string updaterPath = Path.Combine(installDir, UpdaterFileName);
            if (!File.Exists(updaterPath))
            {
                // 被杀软清理、用户手工删除等。更新程序无法凭空出现，只能提示重装。
                error = "更新程序缺失，请重新安装 KfuPet 后再试。";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                // 必须用 ShellExecute + runas：更新程序带 requireAdministrator 清单，
                // 走 CreateProcess（UseShellExecute = false）会直接报 740 起不来。
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = installDir,
                Arguments = $"--action=update --wait-pid={Environment.ProcessId}"
            };

            try
            {
                using Process? process = Process.Start(startInfo);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 用户在 UAC 弹窗上点了「否」：更新没有开始，留在桌宠里就行，别退出。
                error = "已取消管理员授权，更新未开始。";
                return false;
            }
        }

        /// <summary>从注册表读取安装目录；未安装或读取失败时返回空。</summary>
        private static string ReadInstallPath()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InstallKeyPath);
                return key?.GetValue("InstallPath") as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
