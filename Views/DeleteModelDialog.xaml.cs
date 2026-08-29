using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KfuPet.Views
{
    /// <summary>
    /// 删除模型确认对话框：确认按钮倒计时 3 秒后才可点击。
    /// </summary>
    public partial class DeleteModelDialog : Window
    {
        private const int ConfirmCountdownSeconds = 3;

        private readonly DispatcherTimer _countdownTimer;
        private int _countdownLeft;

        /// <summary>确认删除后触发。</summary>
        public event Action? DeleteConfirmed;

        public DeleteModelDialog(string modelName)
        {
            InitializeComponent();

            SummaryText.Text = $"即将永久删除模型「{modelName}」。\n我并不会备份，删掉后只能重新添加。";

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;

            Loaded += (s, e) =>
            {
                PlayEntranceAnimation();
                StartConfirmCountdown();
            };
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

        /// <summary>开始确认按钮的 3 秒倒计时，按钮文案实时显示剩余秒数。</summary>
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

        /// <summary>最终确认：回传调用方执行删除，并关闭窗口。</summary>
        private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmed?.Invoke();
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
