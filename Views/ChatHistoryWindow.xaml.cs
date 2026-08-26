using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Core.Memory;

namespace KfuPet.Views
{
    /// <summary>
    /// 聊天记录窗口：以气泡形式按时间展示短期记忆与归档中的原始对话。
    /// </summary>
    public partial class ChatHistoryWindow : Window
    {
        private readonly IReadOnlyList<ShortMemoryEntry> _entries;

        public ChatHistoryWindow(IReadOnlyList<ShortMemoryEntry> entries)
        {
            _entries = entries;
            InitializeComponent();

            HistoryList.ItemsSource = _entries;
            CountText.Text = $"共 {_entries.Count} 条";
            EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            Loaded += (s, e) =>
            {
                PlayEntranceAnimation();
                PlayItemsEntranceAnimation();
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
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

        /// <summary>对话项逐个淡入上滑（前 12 条错峰，更多条目不叠加延迟避免等待过长）。</summary>
        private void PlayItemsEntranceAnimation()
        {
            // 等容器生成后再播放动画
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                for (var i = 0; i < HistoryList.Items.Count; i++)
                {
                    if (HistoryList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                    {
                        continue;
                    }

                    var item = FindVisualChild<Border>(container);
                    if (item == null)
                    {
                        continue;
                    }

                    var begin = TimeSpan.FromMilliseconds(100 + 50 * Math.Min(i, 12));
                    var duration = TimeSpan.FromMilliseconds(260);

                    item.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });

                    // 模板里的变换实例已被冻结，需要换成新实例才能挂动画
                    var translate = new TranslateTransform();
                    item.RenderTransform = translate;
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(12, 0, duration) { BeginTime = begin, EasingFunction = ease });
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>在视觉树中向下查找第一个指定类型的子元素。</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
