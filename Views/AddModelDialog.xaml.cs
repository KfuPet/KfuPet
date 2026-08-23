using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KfuPet.Views
{
    /// <summary>
    /// 添加模型对话框，收集服务商 / Base URL / API Key / 模型名称。
    /// </summary>
    public partial class AddModelDialog : Window
    {
        /// <summary>确认添加时触发，携带表单数据。</summary>
        public event EventHandler<ModelConfigEventArgs>? ModelConfirmed;

        public AddModelDialog()
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
            var modelName = ModelNameBox.Text.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = "未命名模型";
            }

            ModelConfirmed?.Invoke(this, new ModelConfigEventArgs
            {
                Provider = ProviderBox.Text.Trim(),
                BaseUrl = BaseUrlBox.Text.Trim(),
                ApiKey = ApiKeyBox.Password,
                ModelName = modelName
            });

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

    /// <summary>模型配置表单数据。</summary>
    public class ModelConfigEventArgs : EventArgs
    {
        public string Provider { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
    }
}
