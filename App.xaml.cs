using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using KfuPet.Models;
using KfuPet.Services;
using KfuPet.Services.Ipc;
using KfuPet.Views;

namespace KfuPet
{
    /// <summary>
    /// 应用程序入口，负责启动流程：先显示 SplashWindow，完成后显示 MainWindow。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>启动后静默检查更新的延迟：等启动动画与主窗口淡入结束，避免通知和开场特效抢注意力。</summary>
        private static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(3);

        /// <summary>系统通知的停留时长（毫秒）。</summary>
        private const int NotificationDisplayMilliseconds = 10000;

        private MainWindow? _mainWindow;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private SettingsWindow? _settingsWindow;
        private TrayMenuWindow? _trayMenu;
        private LogPipeServer? _logPipeServer;
        private LogFileWriter? _logFileWriter;
        private Mutex? _mutex;
        private bool _isDarkTheme;
        private readonly Services.ThemeService _themeService = new();
        private readonly UpdateService _updateService = new();

        /// <summary>启动检查发现的新版本结果，供点击系统通知时展示更新弹窗。</summary>
        private UpdateCheckResult? _startupUpdateResult;

        /// <summary>本次运行的日志文件路径，供“打开日志”按钮定位；未启用落盘时为 null。</summary>
        internal string? CurrentLogFilePath => _logFileWriter?.FilePath;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 日志落盘最先启动，启动阶段的日志也要写进文件
            if (LogFileWriter.Prepare())
            {
                _logFileWriter = new LogFileWriter();
                Log.Instance.AttachFileWriter(_logFileWriter);
            }

            var version = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);
            Log.Info($"[启动] KfuPet v{version} 启动");
            if (_logFileWriter != null)
            {
                Log.Info($"[日志] 本次运行日志文件：{_logFileWriter.FilePath}");
            }

            // 检测多开：只允许运行一个实例
            _mutex = new Mutex(true, "KfuPet_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                Log.Warning("[启动] 检测到已有实例在运行，本次启动已退出");
                MessageBox.Show("我已经在桌面上啦，不用再叫醒我一次～", "KfuPet", MessageBoxButton.OK, MessageBoxImage.Information);
                _mutex = null;
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 日志管道常开、尽早启动：不随开发者模式开关，且启动阶段的日志也要能被开发者工具收到。
            // 必须放在单实例检查之后，否则第二个实例会在被拒绝前短暂占用同名管道。
            _logPipeServer = new LogPipeServer(Log.Instance);
            _logPipeServer.Start();
            Log.Info("[IPC] 日志管道已启动");

            // 加载配色令牌：用户手动选过外观则以偏好为准，否则跟随系统深浅色并实时切换
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            if (_themeService.PreferredDark.HasValue)
            {
                ApplyTheme(_themeService.PreferredDark.Value);
            }
            else
            {
                ApplySystemTheme();
            }

            // 主窗口预先创建但保持隐藏，等待 Splash 结束后再显示
            _mainWindow = new MainWindow();

            var splashWindow = new SplashWindow();
            EventHandler? splashHandler = null;
            splashHandler = (s, args) =>
            {
                splashWindow.SplashCompleted -= splashHandler;

                // 启动动画播放完成后显示系统托盘图标
                InitializeTrayIcon();

                _mainWindow.Show();
                _mainWindow.PlayFadeInAnimation();
                Log.Info("[启动] 启动画面已结束，主窗口已显示");

                // 静默检查更新：不阻塞启动流程，检查失败或已是最新都不打扰用户
                _ = CheckUpdateOnStartupAsync();
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

            // 点击系统通知：展示启动检查发现的版本更新
            _notifyIcon.BalloonTipClicked += (s, args) => ShowStartupUpdateDialog();

            Log.Debug("[托盘] 托盘图标已就绪");
        }

        /// <summary>
        /// 启动后静默检查一次更新：已是最新或检查失败都只记日志，
        /// 发现新版本时才通过系统通知告知用户。
        /// </summary>
        private async Task CheckUpdateOnStartupAsync()
        {
            try
            {
                await Task.Delay(StartupUpdateCheckDelay);

                Log.Debug("[更新] 启动静默检查更新开始");

                var result = await _updateService.CheckAsync();
                if (result == null)
                {
                    // 所有更新源都不可用（断网、接口异常等）：静默跳过，不打扰用户
                    Log.Debug("[更新] 启动检查：所有更新源均不可用，本次跳过");
                    return;
                }

                if (!result.IsUpdateAvailable)
                {
                    Log.Debug($"[更新] 启动检查：已是最新（远端 v{result.LatestVersion.ToString(3)}）");
                    return;
                }

                _startupUpdateResult = result;
                Log.Info($"[更新] 启动检查发现新版本 v{result.LatestVersion.ToString(3)}，已发出系统通知");
                ShowUpdateNotification(result);
            }
            catch (Exception ex)
            {
                // 静默检查不得影响启动流程，异常只记录
                Log.Warning($"[更新] 启动检查更新失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 用托盘图标发出系统通知（Windows 10 起会显示为系统通知）。
        /// </summary>
        private void ShowUpdateNotification(UpdateCheckResult result)
        {
            if (_notifyIcon == null)
            {
                return;
            }

            _notifyIcon.BalloonTipTitle = $"发现新版本 v{result.LatestVersion.ToString(3)}";
            _notifyIcon.BalloonTipText = $"当前版本 v{result.CurrentVersion.ToString(3)}，点击查看更新内容。";
            _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(NotificationDisplayMilliseconds);
        }

        /// <summary>
        /// 点击系统通知：展示更新弹窗，流程与设置窗口里的“检查更新”完全一致。
        /// </summary>
        private void ShowStartupUpdateDialog()
        {
            if (_startupUpdateResult is not { IsUpdateAvailable: true } result)
            {
                return;
            }

            Dispatcher.Invoke(() => UpdatePrompt.Show(_mainWindow, result));
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
                    Log.Info("[托盘] 用户选择退出程序");
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
                Log.Info("[窗口] 打开设置窗口");
                _settingsWindow = new SettingsWindow(_mainWindow);
                _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>当前主题偏好：true 深色，false 浅色，null 跟随系统。</summary>
        public bool? ThemePreference => _themeService.PreferredDark;

        /// <summary>
        /// 设置主题偏好并保存：true/false 手动指定深色/浅色，null 恢复跟随系统主题。
        /// </summary>
        public void SetThemePreference(bool? isDark)
        {
            if (isDark.HasValue)
            {
                ApplyTheme(isDark.Value);
            }
            else
            {
                ApplySystemTheme();
            }

            _themeService.SavePreference(isDark);
        }

        /// <summary>
        /// 根据系统深色/浅色模式加载对应的配色令牌资源。
        /// </summary>
        private void ApplySystemTheme()
        {
            ApplyTheme(IsSystemDarkMode());
        }

        /// <summary>
        /// 切换浅色/深色配色令牌，手动切换与跟随系统共用。
        /// </summary>
        private void ApplyTheme(bool isDark)
        {
            if (isDark == _isDarkTheme)
            {
                return;
            }

            var target = isDark ? "Resources/Colors.Dark.xaml" : "Resources/Colors.Light.xaml";
            var dictionaries = Resources.MergedDictionaries;
            for (var i = 0; i < dictionaries.Count; i++)
            {
                var source = dictionaries[i].Source?.OriginalString;
                if (source != null && (source.Contains("Colors.Light.xaml") || source.Contains("Colors.Dark.xaml")))
                {
                    dictionaries[i] = new ResourceDictionary
                    {
                        Source = new Uri(target, UriKind.Relative)
                    };
                    _isDarkTheme = isDark;
                    Log.Info($"[外观] 已切换到{(isDark ? "深色" : "浅色")}主题");
                    break;
                }
            }
        }

        /// <summary>
        /// 系统外观偏好变化（含深色/浅色切换）时，重新应用主题令牌。
        /// </summary>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // 用户手动选过外观后不再跟随系统
            if (_themeService.PreferredDark.HasValue)
            {
                return;
            }

            Dispatcher.InvokeAsync(ApplySystemTheme);
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
            Log.Info("[退出] KfuPet 正在退出，开始清理资源");
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _notifyIcon?.Dispose();
            _logPipeServer?.Stop();
            _logPipeServer?.Dispose();
            _mutex?.Dispose();
            Services.SkeletonService.CleanupCache();

            // 落盘最后收尾，让上面的清理日志也能写进文件
            _logFileWriter?.Dispose();
            base.OnExit(e);
        }
    }
}
