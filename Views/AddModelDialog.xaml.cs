using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 添加模型对话框，收集服务商 / Base URL / API Key / 模型名称。
    /// </summary>
    public partial class AddModelDialog : Window
    {
        private readonly AiConnectivityService _connectivityService = new();

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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var provider = ProviderBox.Text.Trim();
            var baseUrl = BaseUrlBox.Text.Trim();
            var apiKey = ApiKeyBox.Password;

            var modelName = ModelNameBox.Text.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = "未命名模型";
            }

            SetConfirmBusy(true);
            try
            {
                await _connectivityService.TestAsync(baseUrl, apiKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败，请检查服务商、Base URL 与 API Key 是否正确。\n\n{ex.Message}",
                    "KfuPet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                SetConfirmBusy(false);
            }

            ModelConfirmed?.Invoke(this, new ModelConfigEventArgs
            {
                Provider = provider,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelName = modelName
            });

            Close();
        }

        /// <summary>
        /// 切换确认按钮为“验证连接中”的转圈样式，避免重复提交。
        /// </summary>
        private void SetConfirmBusy(bool busy)
        {
            ConfirmText.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            ConfirmSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = !busy;

            if (busy)
            {
                ConfirmSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.5))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
            else
            {
                ConfirmSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        /// <summary>
        /// 已有添加窗口时被再次触发：把窗口带到前台，短暂置顶提醒并播放系统提示音。
        /// </summary>
        public async void FlashToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            PlayReminderSound();

            // 窗口已在前台或已置顶时无需再激活/置顶，避免 Topmost 来回切换造成闪烁
            if (IsActive || Topmost)
            {
                return;
            }

            Activate();
            Topmost = true;
            await Task.Delay(2000);
            Topmost = false;
        }

        /// <summary>
        /// 播放一次系统提示音，失败时静默忽略。
        /// </summary>
        private static void PlayReminderSound()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch
            {
                // 提示音播放失败不影响主流程
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

    /// <summary>模型配置表单数据。</summary>
    public class ModelConfigEventArgs : EventArgs
    {
        public string Provider { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
    }
}
