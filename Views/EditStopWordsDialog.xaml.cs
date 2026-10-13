using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KfuPet.Views
{
    /// <summary>
    /// 通用的“词表编辑”对话框：在大文本域中用中英文逗号或换行分隔。
    /// 默认用于编辑“检索停用词”，也可传入标题、说明与字段名复用于“记忆触发词”等其它词表。
    /// </summary>
    public partial class EditStopWordsDialog : Window
    {
        /// <summary>确认保存时触发，携带编辑后的原始文本。</summary>
        public event Action<string>? StopWordsConfirmed;

        public EditStopWordsDialog(
            string title = "编辑停用词",
            string description = "这里列出我回忆时用来过滤虚词的关键词。用中英文逗号或换行分隔，保存后立即生效。",
            string fieldLabel = "停用词")
        {
            InitializeComponent();
            Title = title;
            DialogTitleText.Text = title;
            DialogDescriptionText.Text = description;
            FieldLabelText.Text = fieldLabel;
            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        /// <summary>用当前词表预填编辑框。</summary>
        public void LoadWords(IReadOnlyList<string> words)
        {
            StopWordsEditor.Text = string.Join("，", words);
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            StopWordsConfirmed?.Invoke(StopWordsEditor.Text);
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
