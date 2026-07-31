using System.Windows;

namespace KfuPet
{
    /// <summary>
    /// 应用程序入口，负责启动流程：先显示 SplashWindow，完成后显示 MainWindow。
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? _mainWindow;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 检测多开：只允许运行一个实例
            _mutex = new Mutex(true, "KfuPet_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("KfuPet 已在运行中。", "KfuPet", MessageBoxButton.OK, MessageBoxImage.Information);
                _mutex = null;
                Shutdown();
                return;
            }

            base.OnStartup(e);

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
        /// 初始化系统托盘图标及右键菜单。
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

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var checkUpdateItem = new System.Windows.Forms.ToolStripMenuItem("检查更新");
            var settingsItem = new System.Windows.Forms.ToolStripMenuItem("设置");
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");

            exitItem.Click += (s, args) =>
            {
                _notifyIcon?.Dispose();
                Shutdown();
            };

            contextMenu.Items.Add(checkUpdateItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
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
