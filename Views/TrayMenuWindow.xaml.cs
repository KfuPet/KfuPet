using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KfuPet.Views
{
    /// <summary>
    /// 托盘右键菜单，WPF 自绘弹出窗口：跟随主题配色，带打开/悬停动效，点击外部自动收起。
    /// </summary>
    public partial class TrayMenuWindow : Window
    {
        /// <summary>点击“检查更新”时触发。</summary>
        public event EventHandler? CheckUpdateClicked;

        /// <summary>点击“设置”时触发。</summary>
        public event EventHandler? SettingsClicked;

        /// <summary>点击“退出”时触发。</summary>
        public event EventHandler? ExitClicked;

        // 用于取消未完成的淡出关闭：每次显示/关闭递增，过期的淡出完成回调直接忽略
        private int _showToken;

        public TrayMenuWindow()
        {
            InitializeComponent();

            Deactivated += (s, e) => CloseMenu();
            MouseDown += OnWindowMouseDown;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CloseMenu();
                }
            };

            CheckUpdateItem.Click += (s, e) => OnItemClicked(CheckUpdateClicked);
            SettingsItem.Click += (s, e) => OnItemClicked(SettingsClicked);
            ExitItem.Click += (s, e) => OnItemClicked(ExitClicked);
        }

        /// <summary>
        /// 在鼠标光标附近显示菜单（贴近任务栏弹出，自动避让屏幕边缘）。
        /// </summary>
        public void ShowNearCursor()
        {
            _showToken++;

            Show();
            UpdateLayout();
            PositionNearCursor();
            Activate();
            PlayEntranceAnimation();

            // 捕获鼠标：点击窗口外任何位置（任务栏、其他应用）都会路由到本窗口，从而收起菜单
            Mouse.Capture(this, CaptureMode.SubTree);
        }

        /// <summary>
        /// 捕获鼠标期间，窗口外点击的坐标会落在窗口边界之外，据此收起菜单。
        /// </summary>
        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            {
                CloseMenu();
            }
        }

        /// <summary>
        /// 根据光标位置计算菜单坐标：默认显示在光标上方，上方放不下时改到下方。
        /// </summary>
        private void PositionNearCursor()
        {
            // WinForms 光标位置是物理像素，需要换算成 WPF 设备无关单位
            var cursorPos = System.Windows.Forms.Cursor.Position;
            var dpi = VisualTreeHelper.GetDpi(this);
            double x = cursorPos.X / dpi.DpiScaleX;
            double y = cursorPos.Y / dpi.DpiScaleY;

            var workArea = SystemParameters.WorkArea;

            if (x + ActualWidth > workArea.Right)
            {
                x = workArea.Right - ActualWidth;
            }
            if (x < workArea.Left)
            {
                x = workArea.Left;
            }

            y = y - ActualHeight >= workArea.Top ? y - ActualHeight : y;

            Left = x;
            Top = y;
        }

        /// <summary>
        /// 打开动画：淡入 + 从下方轻微上浮。
        /// </summary>
        private void PlayEntranceAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            MenuRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });

            if (MenuRoot.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            }
        }

        /// <summary>
        /// 快速淡出后隐藏菜单。
        /// </summary>
        private void CloseMenu()
        {
            if (!IsVisible)
            {
                return;
            }

            int token = ++_showToken;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90));
            fade.Completed += (s, e) =>
            {
                if (token == _showToken)
                {
                    ReleaseMouseCapture();
                    Hide();
                }
            };
            MenuRoot.BeginAnimation(OpacityProperty, fade);
        }

        private void OnItemClicked(EventHandler? handler)
        {
            // 菜单项点击后立即收起，再执行对应动作
            _showToken++;
            ReleaseMouseCapture();
            Hide();
            handler?.Invoke(this, EventArgs.Empty);
        }
    }
}
