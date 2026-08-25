using System.Diagnostics;
using System.Windows;
using KfuPet.Services;
using KfuPet.Views;

namespace KfuPet
{
    /// <summary>
    /// 应用程序入口，负责启动流程：先显示 SplashWindow，完成后显示 MainWindow。
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? _mainWindow;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private SettingsWindow? _settingsWindow;
        private TrayMenuWindow? _trayMenu;
        private Mutex? _mutex;
        private readonly UpdateService _updateService = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            // 检测多开：只允许运行一个实例
            _mutex = new Mutex(true, "KfuPet_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("我已经在桌面上啦，不用再叫醒我一次～", "KfuPet", MessageBoxButton.OK, MessageBoxImage.Information);
                _mutex = null;
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 根据系统主题加载配色令牌
            ApplySystemTheme();

            // 初始化系统托盘图标
            InitializeTrayIcon();

            // 主窗口预先创建但保持隐藏，等待 Splash 结束后再显示
            _mainWindow = new MainWindow();

            var splashWindow = new SplashWindow();
            EventHandler? splashHandler = null;
            splashHandler = (s, args) =>
            {
                splashWindow.SplashCompleted -= splashHandler;
                _mainWindow.Show();
                _mainWindow.PlayFadeInAnimation();
            };
            splashWindow.SplashCompleted += splashHandler;
            splashWindow.Show();
        }

        /// <summary>
        /// 初始化系统托盘图标，右键点击时弹出 WPF 自绘菜单。
        /// </summary>
        private void InitializeTrayIcon()
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon/tray.ico");
            var streamResourceInfo = GetResourceStream(iconUri);

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(streamResourceInfo.Stream),
                Visible = true,
                Text = "KfuPet"
            };

            _notifyIcon.MouseUp += (s, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    ShowTrayMenu();
                }
            };
        }

        /// <summary>
        /// 在光标处显示托盘菜单（单例），并挂接菜单项动作。
        /// </summary>
        private void ShowTrayMenu()
        {
            if (_trayMenu == null)
            {
                _trayMenu = new TrayMenuWindow();
                _trayMenu.SettingsClicked += (s, e) => OpenSettingsWindow();
                _trayMenu.ExitClicked += (s, e) =>
                {
                    _notifyIcon?.Dispose();
                    Shutdown();
                };
                _trayMenu.ModelConfigClicked += (s, e) =>
                {
                    OpenSettingsWindow();
                    _settingsWindow?.ShowModelConfigPage();
                };
            }

            _trayMenu.ShowNearCursor();
        }

        /// <summary>
        /// 打开设置窗口（单例），再次点击时激活已有窗口。
        /// </summary>
        private void OpenSettingsWindow()
        {
            if (_mainWindow == null) return;

            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow(_mainWindow);
                _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>
        /// 检查更新：已是最新时提示，有新版本时先询问用户，确认后用默认浏览器打开发布页面。
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            var result = await _updateService.CheckAsync();

            if (result == null)
            {
                var failDialog = new UpdateDialog(false, "未知", "未知", null)
                {
                    Owner = _settingsWindow
                };
                failDialog.TitleText.Text = "检查更新";
                failDialog.StatusIcon.Text = "\uE783"; // 警告图标
                failDialog.StatusTitleText.Text = "检查更新失败";
                failDialog.StatusDetailText.Text = "请检查网络后重试";
                failDialog.ConfirmButton.Content = "确定";
                failDialog.ShowDialog();
                return;
            }

            var dialog = new UpdateDialog(
                result.IsUpdateAvailable,
                result.CurrentVersion.ToString(3),
                result.LatestVersion.ToString(3),
                result.ReleaseNotes)
            {
                Owner = _settingsWindow
            };

            dialog.DownloadConfirmed += (s, e) => OpenReleasePage(result.ReleasePageUrl);
            dialog.ShowDialog();
        }

        /// <summary>
        /// 用系统默认浏览器打开发布页面。
        /// </summary>
        private static void OpenReleasePage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show($"打开下载页面失败，请手动访问：\n{url}",
                    "KfuPet", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 根据系统深色/浅色模式加载对应的配色令牌资源。
        /// </summary>
        private void ApplySystemTheme()
        {
            if (!IsSystemDarkMode())
            {
                return;
            }

            var dictionaries = Resources.MergedDictionaries;
            for (var i = 0; i < dictionaries.Count; i++)
            {
                var source = dictionaries[i].Source?.OriginalString;
                if (source != null && source.Contains("Colors.Light.xaml"))
                {
                    dictionaries[i] = new ResourceDictionary
                    {
                        Source = new Uri("Resources/Colors.Dark.xaml", UriKind.Relative)
                    };
                    break;
                }
            }
        }

        /// <summary>
        /// 通过注册表检测系统是否使用深色模式。
        /// </summary>
        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            _mutex?.Dispose();
            Services.SkeletonService.CleanupCache();
            base.OnExit(e);
        }
    }
}
