using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Helpers;
using KfuPet.Models;

namespace KfuPet.Views
{
    /// <summary>
    /// 启动公告弹窗：展示公告仓库中的最新公告，正文按 Markdown 渲染。
    /// </summary>
    public partial class AnnouncementDialog : Window
    {
        /// <summary>信息图标。</summary>
        private const string InfoIcon = "\uE946";

        /// <summary>警告图标（warning / important 共用）。</summary>
        private const string WarningIcon = "\uE7BA";

        private readonly string _link;

        public AnnouncementDialog(Announcement announcement)
        {
            InitializeComponent();

            _link = announcement.Link;
            StatusTitleText.Text = announcement.Title;
            DateText.Text = string.IsNullOrWhiteSpace(announcement.Date)
                ? string.Empty
                : $"更新日期：{announcement.Date}";

            ApplyLevel(announcement.Level);

            if (string.IsNullOrWhiteSpace(announcement.Body))
            {
                BodyCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                MarkdownRenderer.Render(announcement.Body, BodyPanel);
            }

            if (Uri.TryCreate(announcement.Link, UriKind.Absolute, out _))
            {
                LinkButton.Visibility = Visibility.Visible;
            }

            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        /// <summary>
        /// 按公告级别设置图标、标签文字与配色。
        /// </summary>
        private void ApplyLevel(string level)
        {
            switch (level)
            {
                case "warning":
                    ApplyLevelStyle("提醒", Color.FromRgb(0xE8, 0xA4, 0x00));
                    break;

                case "important":
                    ApplyLevelStyle("重要", Color.FromRgb(0xE8, 0x11, 0x23));
                    break;
            }
        }

        private void ApplyLevelStyle(string text, Color color)
        {
            LevelText.Text = text;
            StatusIcon.Text = WarningIcon;

            var foreground = new SolidColorBrush(color);
            foreground.Freeze();
            StatusIcon.Foreground = foreground;
            LevelText.Foreground = foreground;

            var background = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B));
            background.Freeze();
            LevelBadge.Background = background;
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
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Uri.TryCreate(_link, UriKind.Absolute, out var uri))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // 打不开浏览器时忽略，不影响弹窗关闭
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
                    new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            }
        }
    }
}
