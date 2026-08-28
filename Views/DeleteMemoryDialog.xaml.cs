using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KfuPet.Views
{
    /// <summary>
    /// 删除记忆对话框：先在同一窗口内勾选要删除的记忆类型，
    /// 再切换到警告页最终确认（确认按钮倒计时 5 秒后才可点）。
    /// </summary>
    public partial class DeleteMemoryDialog : Window
    {
        /// <summary>可删除的记忆类型。</summary>
        [Flags]
        public enum MemoryKinds
        {
            None = 0,
            ShortTerm = 1,
            Archive = 2,
            LongTerm = 4
        }

        private const int ConfirmCountdownSeconds = 5;

        private readonly DispatcherTimer _countdownTimer;
        private int _countdownLeft;

        /// <summary>确认删除后触发，携带要删除的记忆类型。</summary>
        public event Action<MemoryKinds>? DeleteConfirmed;

        public DeleteMemoryDialog()
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
            ContinueButton.IsEnabled = ShortCheck.IsChecked == true
                                    || ArchiveCheck.IsChecked == true
                                    || LongCheck.IsChecked == true;
        }

        /// <summary>继续：在同一窗口内从选择页切换到警告页，并开始确认按钮倒计时。</summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            WarningSummaryText.Text = BuildSummaryText();

            SelectPage.Visibility = Visibility.Collapsed;
            SelectButtons.Visibility = Visibility.Collapsed;
            WarningPage.Visibility = Visibility.Visible;
            WarningButtons.Visibility = Visibility.Visible;

            PlayWarningPageAnimation();
            StartConfirmCountdown();
        }

        /// <summary>按勾选项生成警告摘要文本。</summary>
        private string BuildSummaryText()
        {
            var parts = new System.Text.StringBuilder("即将永久删除：");
            if (ShortCheck.IsChecked == true)
            {
                parts.Append("短期记忆、");
            }
            if (ArchiveCheck.IsChecked == true)
            {
                parts.Append("归档记忆、");
            }
            if (LongCheck.IsChecked == true)
            {
                parts.Append("长期记忆、");
            }
            parts.Length -= 1; // 去掉末尾顿号
            parts.Append("。\n我并不会备份，删掉就真的忘光了。");
            return parts.ToString();
        }

        /// <summary>警告页入场：淡入 + 轻微上滑。</summary>
        private void PlayWarningPageAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(220);

            WarningPage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            if (WarningPage.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
            }
        }

        /// <summary>开始确认按钮的 5 秒倒计时，按钮文案实时显示剩余秒数。</summary>
        private void StartConfirmCountdown()
        {
            _countdownLeft = ConfirmCountdownSeconds;
            ConfirmDeleteButton.IsEnabled = false;
            ConfirmDeleteButton.Content = $"确认删除（{_countdownLeft}）";
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _countdownLeft--;
            if (_countdownLeft <= 0)
            {
                _countdownTimer.Stop();
                ConfirmDeleteButton.Content = "确认删除";
                ConfirmDeleteButton.IsEnabled = true;
                return;
            }

            ConfirmDeleteButton.Content = $"确认删除（{_countdownLeft}）";
        }

        /// <summary>最终确认：把勾选的类型回传给调用方执行删除，并关闭窗口。</summary>
        private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var kinds = MemoryKinds.None;
            if (ShortCheck.IsChecked == true)
            {
                kinds |= MemoryKinds.ShortTerm;
            }
            if (ArchiveCheck.IsChecked == true)
            {
                kinds |= MemoryKinds.Archive;
            }
            if (LongCheck.IsChecked == true)
            {
                kinds |= MemoryKinds.LongTerm;
            }

            DeleteConfirmed?.Invoke(kinds);
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
