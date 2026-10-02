using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KfuPet.Services;

namespace KfuPet
{
    /// <summary>
    /// 启动窗口，展示 Logo 动画与版本号：固定展示后淡出并通知主窗口显示；
    /// 若固定展示时间已到而主窗内容仍未就绪，则保持动画结束状态等待，就绪后再淡出（不循环播放）。
    /// </summary>
    public partial class SplashWindow : Window
    {
        /// <summary>
        /// 标题与副标题之间空隙的目标宽度（设备无关像素）。
        /// </summary>
        private const double TextGap = 16;

        /// <summary>
        /// 固定展示时长（秒）：主窗内容在此之前就绪也照常等满。
        /// </summary>
        private const int CountdownSeconds = 4;

        /// <summary>
        /// 启动动画完成（含淡出）时触发，主窗口监听此事件后显示。
        /// </summary>
        public event EventHandler? SplashCompleted;

        /// <summary>固定展示时间是否已到点（此时入场动画早已播完，画面处于静止状态）。</summary>
        private bool _countdownElapsed;

        /// <summary>主窗内容是否已就绪（由 <see cref="SetContentReady"/> 置位）。</summary>
        private bool _contentReady;

        /// <summary>等待主窗内容就绪的计时器，仅用于记录等待时长。</summary>
        private readonly Stopwatch _waitStopwatch = new();

        public SplashWindow()
        {
            InitializeComponent();
            LoadVersion();
            SetupMask();
            AlignTextGapToCenter();
            StartEntranceAnimation();
            StartCountdown();
        }

        private void StartEntranceAnimation()
        {
            var storyboard = (Storyboard)RootGrid.Resources["FadeInStoryboard"];
            storyboard.Begin();
        }

        /// <summary>
        /// 根据实际文字宽度计算标题、遮罩与副标题位移动画的最终偏移，
        /// 使标题与副标题之间空隙的中心与上方 Logo 的中心线对齐。
        /// 必须在入场动画开始前调用。
        /// </summary>
        private void AlignTextGapToCenter()
        {
            double titleWidth = MeasureTextWidth(TitleText);
            double subtitleWidth = MeasureTextWidth(SubtitleText);

            double titleX = -(TextGap + titleWidth) / 2;
            double subtitleX = (TextGap + subtitleWidth) / 2;

            // 遮罩右边缘（含渐变收尾区）最终要滑过文字空隙右端，
            // 否则渐变尾部会压住副标题文字左侧
            double maskRestRight = SubtitleMask.Margin.Left + SubtitleMask.Width;
            double gapRight = ((FrameworkElement)SubtitleMask.Parent).Width / 2 + TextGap / 2;
            double maskX = gapRight - maskRestRight - 2;

            var storyboard = (Storyboard)RootGrid.Resources["FadeInStoryboard"];
            foreach (var timeline in storyboard.Children)
            {
                if (timeline is DoubleAnimation animation
                    && Storyboard.GetTargetProperty(animation)?.Path == "X")
                {
                    switch (Storyboard.GetTargetName(animation))
                    {
                        case "TitleTransform":
                            animation.To = titleX;
                            break;
                        case "MaskTransform":
                            animation.To = maskX;
                            break;
                        case "SubtitleTransform":
                            animation.To = subtitleX;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// 测量 TextBlock 文字的实际渲染宽度。
        /// </summary>
        private static double MeasureTextWidth(TextBlock textBlock)
        {
            var formattedText = new FormattedText(
                textBlock.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                textBlock.FontSize,
                Brushes.Black,
                pixelsPerDip: 1.0);
            return formattedText.Width;
        }

        /// <summary>
        /// 根据根卡片背景色构建渐变遮罩，用于副标题文字的滑动显隐效果。
        /// </summary>
        private void SetupMask()
        {
            if (RootCard.Background is SolidColorBrush brush)
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
        /// 主窗内容加载完成：固定展示时间已到则立即淡出结束，未到则照常等满固定时长。
        /// </summary>
        public void SetContentReady()
        {
            _contentReady = true;
            if (_countdownElapsed)
            {
                _waitStopwatch.Stop();
                Log.Info($"[启动画面] 主窗内容已就绪，开始淡出（已等待 {_waitStopwatch.ElapsedMilliseconds} ms）");
                StartFadeOut();
            }
        }

        /// <summary>
        /// 固定展示时长到点后开始淡出；主窗内容还没就绪时保持动画结束状态等待，
        /// 由 <see cref="SetContentReady"/> 触发淡出。淡出完成后触发 SplashCompleted 事件并关闭窗口。
        /// </summary>
        private void StartCountdown()
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(CountdownSeconds);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _countdownElapsed = true;
                if (_contentReady)
                {
                    StartFadeOut();
                }
                else
                {
                    // 主窗内容未就绪：保持动画结束画面等待（不循环），就绪后再淡出
                    _waitStopwatch.Start();
                    Log.Info("[启动画面] 固定展示结束，主窗内容未就绪，保持动画结束画面等待");
                }
            };
            timer.Start();
        }

        private void StartFadeOut()
        {
            var storyboard = (Storyboard)RootGrid.Resources["FadeOutStoryboard"];
            storyboard.Completed += FadeOutStoryboard_Completed;
            storyboard.Begin();
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
