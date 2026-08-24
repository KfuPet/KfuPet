using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KfuPet.Views
{
    /// <summary>
    /// 检查更新结果弹窗，展示当前/最新版本、更新日志，并提供前往下载按钮。
    /// </summary>
    public partial class UpdateDialog : Window
    {
        /// <summary>用户确认前往下载时触发。</summary>
        public event EventHandler? DownloadConfirmed;

        private readonly bool _hasUpdate;

        public UpdateDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }

        public UpdateDialog(bool hasUpdate, string currentVersion, string latestVersion, string? releaseNotes)
            : this()
        {
            _hasUpdate = hasUpdate;

            if (hasUpdate)
            {
                StatusIcon.Text = "\uE72C"; // 下载图标
                StatusIcon.Foreground = (Brush)FindResource("AppAccentBrush");
                StatusTitleText.Text = $"发现新版本 v{latestVersion}";
                StatusDetailText.Text = $"当前版本 v{currentVersion}";
                ConfirmButton.Content = "前往下载";
                CancelButton.Visibility = Visibility.Visible;

                if (!string.IsNullOrWhiteSpace(releaseNotes))
                {
                    ReleaseNotesText.Text = releaseNotes;
                    ReleaseNotesPanel.Visibility = Visibility.Visible;
                }
            }
            else
            {
                StatusIcon.Text = "\uE73E"; // 勾选图标
                StatusIcon.Foreground = (Brush)FindResource("AppAccentBrush");
                StatusTitleText.Text = "已是最新版本";
                StatusDetailText.Text = $"当前版本 v{currentVersion}";
                ConfirmButton.Content = "确定";
            }

            LoadingBar.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 显示"检查中"状态。
        /// </summary>
        public void SetCheckingState()
        {
            StatusIcon.Text = "\uE72C";
            StatusTitleText.Text = "正在检查更新...";
            StatusDetailText.Text = "请稍候";
            LoadingBar.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUpdate)
            {
                DownloadConfirmed?.Invoke(this, EventArgs.Empty);
            }

            DialogResult = true;
            Close();
        }

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
