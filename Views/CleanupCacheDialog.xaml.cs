using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KfuPet.Views
{
    /// <summary>
    /// 缓存清理对话框：先在同一窗口内勾选要清理的内容（历史日志、软件配置、记忆），
    /// 再切换到警告页最终确认（确认按钮倒计时 5 秒后才可点）。
    /// </summary>
    public partial class CleanupCacheDialog : Window
    {
        /// <summary>可清理的缓存类型。</summary>
        [Flags]
        public enum CacheKinds
        {
            None = 0,
            Logs = 1,
            Config = 2,
            Memory = 4
        }

        /// <summary>两项及以上内容一起清理时的确认等待秒数。</summary>
        private const int MultiItemCountdownSeconds = 5;

        /// <summary>单项软件配置 / 记忆的确认等待秒数（只清理日志无需等待）。</summary>
        private const int SingleItemCountdownSeconds = 3;

        private readonly DispatcherTimer _countdownTimer;
        private int _countdownLeft;

        /// <summary>确认清理后触发，携带要清理的类型。</summary>
        public event Action<CacheKinds>? CleanupConfirmed;

        public CleanupCacheDialog()
        {
            InitializeComponent();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;

            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
            Closed += (s, e) => _countdownTimer.Stop();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>任一勾选项变化：至少勾一项才能继续。</summary>
        private void AnyCheck_Changed(object sender, RoutedEventArgs e)
        {
            ContinueButton.IsEnabled = LogCheck.IsChecked == true
                                    || ConfigCheck.IsChecked == true
                                    || MemoryCheck.IsChecked == true;
        }

        /// <summary>继续：在同一窗口内从选择页切换到警告页，并按勾选内容开始确认倒计时。</summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            WarningSummaryText.Text = BuildSummaryText();

            SelectPage.Visibility = Visibility.Collapsed;
            SelectButtons.Visibility = Visibility.Collapsed;
            WarningPage.Visibility = Visibility.Visible;
            WarningButtons.Visibility = Visibility.Visible;

            PlayPageAnimation(WarningPage);
            StartConfirmCountdown(GetConfirmCountdownSeconds());
        }

        /// <summary>警告页“取消”：返回选择页继续调整勾选，而不是关闭窗口。</summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            ConfirmCleanupButton.IsEnabled = false;
            ConfirmCleanupButton.Content = "确认清理";

            WarningPage.Visibility = Visibility.Collapsed;
            WarningButtons.Visibility = Visibility.Collapsed;
            SelectPage.Visibility = Visibility.Visible;
            SelectButtons.Visibility = Visibility.Visible;

            PlayPageAnimation(SelectPage);
        }

        /// <summary>
        /// 按勾选内容决定确认等待秒数：只清理日志无需等待；
        /// 单项软件配置 / 记忆等 3 秒；两项及以上等 5 秒。
        /// </summary>
        private int GetConfirmCountdownSeconds()
        {
            var selectedCount = 0;
            if (LogCheck.IsChecked == true) selectedCount++;
            if (ConfigCheck.IsChecked == true) selectedCount++;
            if (MemoryCheck.IsChecked == true) selectedCount++;

            if (selectedCount == 1 && LogCheck.IsChecked == true)
            {
                return 0;
            }

            return selectedCount >= 2 ? MultiItemCountdownSeconds : SingleItemCountdownSeconds;
        }

        /// <summary>按勾选项生成警告摘要：软件配置与记忆各自附上对应的提醒。</summary>
        private string BuildSummaryText()
        {
            var parts = new StringBuilder("即将清理：");
            if (LogCheck.IsChecked == true)
            {
                parts.Append("历史日志、");
            }
            if (ConfigCheck.IsChecked == true)
            {
                parts.Append("软件配置、");
            }
            if (MemoryCheck.IsChecked == true)
            {
                parts.Append("全部记忆、");
            }
            parts.Length -= 1; // 去掉末尾顿号
            parts.Append("。\n");

            if (ConfigCheck.IsChecked == true)
            {
                parts.Append("清理软件配置会使软件恢复初始状态，软件会在清理后自动重启。\n");
            }
            if (MemoryCheck.IsChecked == true)
            {
                parts.Append("即将永久删除：短期记忆、归档记忆、长期记忆。我并不会备份，删掉就真的忘光了。\n");
            }
            if (LogCheck.IsChecked == true)
            {
                parts.Append("正在写入的这一份日志会保留。");
            }

            return parts.ToString().TrimEnd('\n');
        }

        /// <summary>选择页 / 警告页切换入场：淡入 + 轻微上滑。</summary>
        private static void PlayPageAnimation(StackPanel page)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(220);

            page.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            if (page.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
            }
        }

        /// <summary>开始确认按钮倒计时，按钮文案实时显示剩余秒数；秒数为 0 时无需等待、立即可点。</summary>
        private void StartConfirmCountdown(int seconds)
        {
            _countdownLeft = seconds;

            if (seconds <= 0)
            {
                ConfirmCleanupButton.Content = "确认清理";
                ConfirmCleanupButton.IsEnabled = true;
                return;
            }

            ConfirmCleanupButton.IsEnabled = false;
            ConfirmCleanupButton.Content = $"确认清理（{_countdownLeft}）";
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _countdownLeft--;
            if (_countdownLeft <= 0)
            {
                _countdownTimer.Stop();
                ConfirmCleanupButton.Content = "确认清理";
                ConfirmCleanupButton.IsEnabled = true;
                return;
            }

            ConfirmCleanupButton.Content = $"确认清理（{_countdownLeft}）";
        }

        /// <summary>最终确认：把勾选的类型回传给调用方执行清理，并关闭窗口。</summary>
        private void ConfirmCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            var kinds = CacheKinds.None;
            if (LogCheck.IsChecked == true)
            {
                kinds |= CacheKinds.Logs;
            }
            if (ConfigCheck.IsChecked == true)
            {
                kinds |= CacheKinds.Config;
            }
            if (MemoryCheck.IsChecked == true)
            {
                kinds |= CacheKinds.Memory;
            }

            CleanupConfirmed?.Invoke(kinds);
            Close();
        }

        /// <summary>窗口打开时的淡入 + 轻微放大动画。</summary>
        private void PlayEntranceAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DialogRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

            if (DialogRoot.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            }
        }
    }
}