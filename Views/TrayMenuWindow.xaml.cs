using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

        // ── Win32：全局低级鼠标钩子 ──────────────────────
        // 菜单以非激活方式弹出（ShowActivated=False），不抢占前台焦点，
        // 系统托盘栏因此不会失焦收起；改用全局鼠标钩子监听“点击菜单窗口外”来收起菜单。
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;

        private IntPtr _hookHandle;
        private HookProc? _hookProc;
        private RECT _screenBounds; // 菜单窗口在屏幕上的物理像素矩形，供钩子回调判断点击是否落在窗口外

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        public TrayMenuWindow()
        {
            InitializeComponent();

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

            Closed += (s, e) => StopMouseHook();
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
            PlayEntranceAnimation();

            // 菜单以非激活状态显示，用全局鼠标钩子监听外部点击来收起
            StartMouseHook();
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
                    StopMouseHook();
                    Hide();
                }
            };
            MenuRoot.BeginAnimation(OpacityProperty, fade);
        }

        private void OnItemClicked(EventHandler? handler)
        {
            // 菜单项点击后立即收起，再执行对应动作
            _showToken++;
            StopMouseHook();
            Hide();
            handler?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 安装全局低级鼠标钩子，用于检测“点击菜单窗口外”并收起菜单。
        /// </summary>
        private void StartMouseHook()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            GetWindowRect(hwnd, out _screenBounds);

            _hookProc = MouseHookProc;
            _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        }

        /// <summary>
        /// 卸载全局鼠标钩子。
        /// </summary>
        private void StopMouseHook()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _hookProc = null;
        }

        /// <summary>
        /// 全局鼠标钩子回调：按下事件落在菜单窗口矩形外时，切回 UI 线程收起菜单。
        /// </summary>
        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN ||
                    msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    bool isOutside = data.pt.X < _screenBounds.Left ||
                                     data.pt.X > _screenBounds.Right ||
                                     data.pt.Y < _screenBounds.Top ||
                                     data.pt.Y > _screenBounds.Bottom;

                    if (isOutside)
                    {
                        Dispatcher.BeginInvoke(new Action(CloseMenu));
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }
}
