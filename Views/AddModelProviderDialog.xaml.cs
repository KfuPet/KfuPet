using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 添加模型对话框：第一页选择服务商（自定义 / DeepSeek / Kimi CN），
    /// 第二页通过返回按钮往返的表单页填写模型与 API Key，确认后触发 ModelConfirmed。
    /// </summary>
    public partial class AddModelProviderDialog : Window
    {
        /// <summary>DeepSeek 服务商预设。</summary>
        public static readonly ModelPreset DeepSeekPreset = new(
            "DeepSeek",
            "https://api.deepseek.com",
            "https://platform.deepseek.com/api_keys",
            "pack://application:,,,/Assets/Providers/deepseek.ico",
            new[] { "deepseek-v4-flash", "deepseek-v4-pro" });

        /// <summary>Kimi CN（Moonshot）服务商预设。</summary>
        public static readonly ModelPreset KimiCnPreset = new(
            "Kimi",
            "https://api.moonshot.cn/v1",
            "https://platform.moonshot.cn/console/api-keys",
            "pack://application:,,,/Assets/Providers/kimi.ico",
            new[] { "kimi-k3", "kimi-k2.6", "kimi-k2.5" });

        private readonly AiConnectivityService _connectivityService = new();

        /// <summary>当前表单页对应的服务商预设；null 表示自定义模型。</summary>
        private ModelPreset? _currentPreset;

        /// <summary>确认添加时触发，携带表单数据。</summary>
        public event EventHandler<ModelConfigEventArgs>? ModelConfirmed;

        public AddModelProviderDialog()
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

        private void CustomModelButton_Click(object sender, RoutedEventArgs e)
        {
            ShowForm(null);
        }

        private void DeepSeekButton_Click(object sender, RoutedEventArgs e)
        {
            ShowForm(DeepSeekPreset);
        }

        private void KimiCnButton_Click(object sender, RoutedEventArgs e)
        {
            ShowForm(KimiCnPreset);
        }

        /// <summary>
        /// 切换到表单页并应用对应服务商预设，重置表单内容。
        /// </summary>
        private void ShowForm(ModelPreset? preset)
        {
            _currentPreset = preset;
            ResetForm();

            // 服务商展示：预设显示 logo + 名称；自定义模型名称可编辑
            var isCustom = preset == null;
            ProviderReadonlyPanel.Visibility = isCustom ? Visibility.Collapsed : Visibility.Visible;
            ProviderNameBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ProviderNameText.Text = preset?.ProviderName ?? string.Empty;
            ProviderLogo.Source = preset?.LogoPath == null ? null : new BitmapImage(new Uri(preset.LogoPath));
            ProviderLogo.Visibility = preset?.LogoPath == null ? Visibility.Collapsed : Visibility.Visible;

            GetApiKeyLink.Visibility = preset != null ? Visibility.Visible : Visibility.Collapsed;
            BaseUrlPanel.Visibility = preset == null ? Visibility.Visible : Visibility.Collapsed;
            ModelTextBox.Visibility = preset == null ? Visibility.Visible : Visibility.Collapsed;
            ModelCombo.Visibility = preset != null ? Visibility.Visible : Visibility.Collapsed;

            ModelCombo.Items.Clear();
            if (preset != null)
            {
                foreach (var model in preset.Models)
                {
                    ModelCombo.Items.Add(new ComboBoxItem { Content = model });
                }
            }

            ProviderListPanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility = Visibility.Visible;
            ButtonBar.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            DialogTitleText.Text = "通过服务商添加";
            AnimatePageSwitch(FormPanel, fromX: 16);
        }

        /// <summary>
        /// 返回服务商列表页。
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            FormPanel.Visibility = Visibility.Collapsed;
            ButtonBar.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            ProviderListPanel.Visibility = Visibility.Visible;
            DialogTitleText.Text = "添加模型";
            HideError();
            AnimatePageSwitch(ProviderListPanel, fromX: -16);
        }

        /// <summary>
        /// 页面切换动效：淡入 + 轻微横向滑动。
        /// </summary>
        private static void AnimatePageSwitch(FrameworkElement element, double fromX)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(220);

            element.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            if (element.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                element.RenderTransform = translate;
            }
            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(fromX, 0, duration) { EasingFunction = ease });
        }

        /// <summary>
        /// 打开当前服务商的 API Key 申请页面。
        /// </summary>
        private void GetApiKeyLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var url = _currentPreset?.ApiKeysPageUrl;
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // 打不开浏览器时不影响主流程
            }
        }

        /// <summary>
        /// 重置按钮：清空当前表单输入。
        /// </summary>
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
        }

        /// <summary>
        /// 清空表单输入（服务商本身由入口决定，不在此处重置）。
        /// </summary>
        private void ResetForm()
        {
            ModelCombo.SelectedItem = null;
            ModelTextBox.Clear();
            BaseUrlBox.Clear();
            ApiKeyBox.Clear();
            if (_currentPreset == null)
            {
                ProviderNameBox.Text = "自定义模型";
            }
            HideError();
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var preset = _currentPreset;
            var baseUrl = preset?.BaseUrl ?? BaseUrlBox.Text.Trim();

            string? modelId;
            if (preset != null)
            {
                modelId = (ModelCombo.SelectedItem as ComboBoxItem)?.Content as string;
                if (string.IsNullOrEmpty(modelId))
                {
                    ShowError("请选择模型");
                    return;
                }
            }
            else
            {
                modelId = ModelTextBox.Text.Trim();
                if (string.IsNullOrEmpty(modelId))
                {
                    ShowError("模型 ID 不能为空，请填写服务商支持的模型标识");
                    return;
                }
            }

            var apiKey = ApiKeyBox.Password;
            var providerName = preset != null ? preset.ProviderName : ProviderNameBox.Text.Trim();

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
                ModelName = string.IsNullOrEmpty(providerName) ? modelId : providerName
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
        /// 已有添加窗口时被再次触发：把它恢复到初始位置并带到前台。
        /// </summary>
        public void FlashToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

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

        private void PlayEntranceAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(280);
            DialogRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            if (DialogRoot.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
            }
        }
    }

    /// <summary>服务商预设：提供商名称、Base URL、API Key 申请页、logo 资源路径与可选模型列表。</summary>
    public class ModelPreset
    {
        public ModelPreset(string providerName, string baseUrl, string apiKeysPageUrl, string? logoPath, IReadOnlyList<string> models)
        {
            ProviderName = providerName;
            BaseUrl = baseUrl;
            ApiKeysPageUrl = apiKeysPageUrl;
            LogoPath = logoPath;
            Models = models;
        }

        public string ProviderName { get; }
        public string BaseUrl { get; }
        public string ApiKeysPageUrl { get; }
        public string? LogoPath { get; }
        public IReadOnlyList<string> Models { get; }
    }
}
