using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KfuPet
{
    /// <summary>
    /// 启动窗口，展示 Logo 动画与版本号，4 秒后淡出并通知主窗口显示。
    /// </summary>
    public partial class SplashWindow : Window
    {
        /// <summary>
        /// 启动动画完成（含淡出）时触发，主窗口监听此事件后显示。
        /// </summary>
        public event EventHandler? SplashCompleted;

        public SplashWindow()
        {
            InitializeComponent();
            ApplySystemTheme();
            LoadVersion();
            SetupMask();
            StartEntranceAnimation();
            StartCountdown();
        }

        /// <summary>
        /// 读取 Windows 个性化设置中的深色/浅色模式并应用主题色。
        /// </summary>
        private void ApplySystemTheme()
        {
            bool isDark = IsSystemDarkMode();
            var bgColor = isDark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;
            var titleFg = isDark ? Brushes.White : Brushes.Black;
            var subtitleFg = isDark
                ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                : new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0x9B));

            RootGrid.Background = new SolidColorBrush(bgColor);
            TitleText.Foreground = titleFg;
            SubtitleText.Foreground = subtitleFg;
            VersionText.Foreground = subtitleFg;
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

        private void StartEntranceAnimation()
        {
            var storyboard = (Storyboard)RootGrid.Resources["FadeInStoryboard"];
            storyboard.Begin();
        }

        /// <summary>
        /// 根据根网格背景色构建渐变遮罩，用于副标题文字的滑动显隐效果。
        /// </summary>
        private void SetupMask()
        {
            if (RootGrid.Background is SolidColorBrush brush)
            {
                var color = brush.Color;
                var transparent = Color.FromArgb(0, color.R, color.G, color.B);

                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0)
                };
                gradient.GradientStops.Add(new GradientStop { Offset = 0, Color = color });
                gradient.GradientStops.Add(new GradientStop { Offset = 0.92, Color = color });
                gradient.GradientStops.Add(new GradientStop { Offset = 1, Color = transparent });

                SubtitleMask.Fill = gradient;
            }
            // 如果不是 SolidColorBrush（理论上在当前配置下不会发生），遮罩效果静默跳过
        }

        /// <summary>
        /// 从程序集版本读取版本号并显示。
        /// </summary>
        private void LoadVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is not null)
            {
                VersionText.Text = $"v{version.ToString(3)}";
            }
        }

        /// <summary>
        /// 4 秒后开始淡出动画，完成后触发 SplashCompleted 事件并关闭窗口。
        /// </summary>
        private void StartCountdown()
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(4);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var storyboard = (Storyboard)RootGrid.Resources["FadeOutStoryboard"];
                storyboard.Completed += FadeOutStoryboard_Completed;
                storyboard.Begin();
            };
            timer.Start();
        }

        private void FadeOutStoryboard_Completed(object? sender, EventArgs e)
        {
            var storyboard = (Storyboard)RootGrid.Resources["FadeOutStoryboard"];
            storyboard.Completed -= FadeOutStoryboard_Completed;
            SplashCompleted?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
