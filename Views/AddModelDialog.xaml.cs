using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Models;
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

        /// <summary>
        /// 编辑模式构造函数：用已有模型配置预填表单。
        /// </summary>
        public AddModelDialog(ModelConfig model) : this()
        {
            LoadModel(model);
        }

        /// <summary>
        /// 用已有模型配置填充表单，并切换标题为“编辑模型”。
        /// </summary>
        public void LoadModel(ModelConfig model)
        {
            BaseUrlBox.Text = model.BaseUrl;
            ApiKeyBox.Password = model.ApiKey;
            ModelIdBox.Text = model.ModelId;
            EmbeddingModelIdBox.Text = model.EmbeddingModelId;
            ModelNameBox.Text = model.ModelName;
            DialogTitleText.Text = "编辑模型";
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
            var baseUrl = BaseUrlBox.Text.Trim();
            var apiKey = ApiKeyBox.Password;

            var modelId = ModelIdBox.Text.Trim();
            if (string.IsNullOrEmpty(modelId))
            {
                ShowError("模型 ID 不能为空，请填写服务商支持的模型标识（例如 deepseek-v4-pro）");
                return;
            }

            var modelName = ModelNameBox.Text.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = "未命名模型";
            }

            var embeddingModelId = EmbeddingModelIdBox.Text.Trim();

            SetConfirmBusy(true);
            HideError();
            try
            {
                await _connectivityService.TestAsync(baseUrl, apiKey);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }
            finally
            {
                SetConfirmBusy(false);
            }

            ModelConfirmed?.Invoke(this, new ModelConfigEventArgs
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelId = modelId,
                EmbeddingModelId = embeddingModelId,
                ModelName = modelName
            });

            Close();
        }

        /// <summary>
        /// 显示连接失败横幅，带淡入 + 下滑动画。
        /// </summary>
        private void ShowError(string detail)
        {
            ErrorDetailText.Text = detail;
            ErrorDetailText.ToolTip = detail;
            ErrorBanner.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ErrorBanner.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

            if (ErrorBanner.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            }
        }

        /// <summary>
        /// 隐藏连接失败横幅（重新发起验证时调用）。
        /// </summary>
        private void HideError()
        {
            ErrorBanner.Visibility = Visibility.Collapsed;
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
        /// 已有添加窗口时被再次触发：把它恢复到初始位置并带到前台，同时播放提示音。
        /// </summary>
        public void FlashToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            PlayReminderSound();

            // 回到程序默认打开的位置（屏幕居中），而不是单纯置顶（置顶切换会造成闪烁）
            CenterToDefaultPosition();

            Activate();
        }

        /// <summary>
        /// 把窗口居中到屏幕工作区，即程序默认打开的位置。
        /// </summary>
        private void CenterToDefaultPosition()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
            Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
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
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string EmbeddingModelId { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
    }
}
