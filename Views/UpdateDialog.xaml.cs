using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Helpers;

namespace KfuPet.Views
{
    /// <summary>
    /// 检查更新结果弹窗，展示当前/最新版本、更新日志，并提供立即更新按钮。
    /// </summary>
    public partial class UpdateDialog : Window
    {
        /// <summary>用户确认立即更新时触发。</summary>
        public event EventHandler? UpdateConfirmed;

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
                ConfirmButton.Content = "立即更新";
                CancelButton.Visibility = Visibility.Visible;

                if (!string.IsNullOrWhiteSpace(releaseNotes))
                {
                    MarkdownRenderer.Render(releaseNotes, ReleaseNotesPanelContent);
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
            // 先关掉弹窗再通知调用方：确认更新的处理程序会拉起更新程序并退出桌宠，
            // 若窗口已随应用退出而关闭，事后再设 DialogResult 会抛异常。
            // 设置 DialogResult 本身就会关闭本窗口。
            var hasUpdate = _hasUpdate;
            DialogResult = true;

            if (hasUpdate)
            {
                UpdateConfirmed?.Invoke(this, EventArgs.Empty);
            }
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
