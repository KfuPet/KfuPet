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
        /// <summary>DeepSeek 服务商预设（模型列表由 /models 接口动态拉取）。</summary>
        public static readonly ModelPreset DeepSeekPreset = new(
            "DeepSeek",
            "https://api.deepseek.com",
            "https://platform.deepseek.com/api_keys",
            "pack://application:,,,/Assets/Providers/deepseek.ico");

        /// <summary>Kimi CN（Moonshot）服务商预设（模型列表由 /models 接口动态拉取）。</summary>
        public static readonly ModelPreset KimiCnPreset = new(
            "Kimi",
            "https://api.moonshot.cn/v1",
            "https://platform.moonshot.cn/console/api-keys",
            "pack://application:,,,/Assets/Providers/kimi.ico");

        private readonly AiConnectivityService _connectivityService = new();

        /// <summary>当前表单页对应的服务商预设；null 表示自定义模型。</summary>
        private ModelPreset? _currentPreset;

        /// <summary>最近一次已尝试拉取自定义模型列表的（Base URL, API Key）组合，避免重复请求。</summary>
        private (string BaseUrl, string ApiKey)? _lastModelFetch;

        /// <summary>预设服务商是否正在拉取模型列表（拉取期间刷新按钮禁用并旋转）。</summary>
        private bool _isFetchingModels;

        /// <summary>确认添加时触发，携带表单数据。</summary>
        public event EventHandler<ModelConfigEventArgs>? ModelConfirmed;

        public AddModelProviderDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => PlayEntranceAnimation();
            Loaded += (s, e) => PlayProviderItemsStagger();
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

            ProviderListPanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility = Visibility.Visible;
            ButtonBar.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            DialogTitleText.Text = "通过服务商添加";
            AnimatePageSwitch(FormPanel, fromX: 16);
            PlayFormFieldsStagger();
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
            PlayProviderItemsStagger();
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
        /// 清空表单输入，并把模型输入恢复到当前流程的初始状态
        /// （预设服务商回到“先刷新获取列表”，自定义模型保持手动输入）。
        /// </summary>
        private void ResetForm()
        {
            ModelCombo.SelectedItem = null;
            ModelCombo.Items.Clear();
            ModelCustomCombo.SelectedItem = null;
            ModelCustomCombo.Text = string.Empty;
            ModelCustomCombo.Items.Clear();
            _lastModelFetch = null;

            if (_currentPreset != null)
            {
                // 预设：先显示占位提示，点击刷新按钮拉到列表后才切换成下拉框
                ModelFetchPanel.Visibility = Visibility.Visible;
                ModelCombo.Visibility = Visibility.Collapsed;
                ModelCustomCombo.Visibility = Visibility.Collapsed;
                StopRefreshSpin();
            }
            else
            {
                ModelFetchPanel.Visibility = Visibility.Collapsed;
                ModelCombo.Visibility = Visibility.Collapsed;
                ModelCustomCombo.Visibility = Visibility.Visible;
            }

            BaseUrlBox.Clear();
            ApiKeyBox.Clear();
            if (_currentPreset == null)
            {
                ProviderNameBox.Text = "自定义模型";
            }
            UpdateRefreshButtonState();
            HideError();
        }

        /// <summary>
        /// 刷新按钮：用当前填写的 API 密钥拉取服务商真实模型列表，
        /// 成功后占位提示切换为可展开的模型下拉框。
        /// </summary>
        private async void ModelRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var preset = _currentPreset;
            var apiKey = ApiKeyBox.Password;
            if (preset == null || string.IsNullOrEmpty(apiKey) || _isFetchingModels) return;

            SetRefreshBusy(true);
            HideError();
            try
            {
                var modelIds = await _connectivityService.GetModelIdsAsync(preset.BaseUrl, apiKey);
                if (_currentPreset != preset) return;

                if (modelIds.Count == 0)
                {
                    ShowError("服务商没有返回可用的模型列表");
                    return;
                }

                FillModelItems(ModelCombo, modelIds);
                ModelFetchPanel.Visibility = Visibility.Collapsed;
                ModelCombo.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Warning($"[配置] 获取模型列表失败：{ex.Message}");
                ShowError(ex.Message);
            }
            finally
            {
                SetRefreshBusy(false);
            }
        }

        /// <summary>
        /// 自定义模型：用已填写的 Base URL 与密钥在后台拉取模型列表填充候选项，
        /// 失败时保持现状（不影响手动输入）。
        /// </summary>
        private async Task TryFetchCustomModelsAsync()
        {
            if (_currentPreset != null) return;

            var baseUrl = BaseUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl)) return;

            var apiKey = ApiKeyBox.Password;
            var signature = (baseUrl, apiKey);
            if (_lastModelFetch == signature) return;
            _lastModelFetch = signature;

            try
            {
                var modelIds = await _connectivityService.GetModelIdsAsync(baseUrl, apiKey);
                if (modelIds.Count == 0) return;

                var current = ModelCustomCombo.Text;
                FillModelItems(ModelCustomCombo, modelIds);
                ModelCustomCombo.Text = current;
            }
            catch (Exception ex)
            {
                // 拉取失败不影响手动填写，清空签名以便下次交互时重试
                _lastModelFetch = null;
                Log.Warning($"[配置] 获取模型列表失败：{ex.Message}");
            }
        }

        /// <summary>用模型 ID 列表替换下拉框候选项。</summary>
        private static void FillModelItems(ComboBox combo, IReadOnlyList<string> modelIds)
        {
            combo.Items.Clear();
            foreach (var id in modelIds)
            {
                combo.Items.Add(id);
            }
        }

        /// <summary>
        /// 切换刷新按钮的忙碌状态：拉取期间禁用按钮并让图标持续旋转。
        /// </summary>
        private void SetRefreshBusy(bool busy)
        {
            _isFetchingModels = busy;
            UpdateRefreshButtonState();

            if (ModelRefreshIcon.RenderTransform is not RotateTransform rotate) return;

            if (busy)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        /// <summary>停止刷新图标旋转并复位。</summary>
        private void StopRefreshSpin()
        {
            if (ModelRefreshIcon.RenderTransform is RotateTransform rotate)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        /// <summary>刷新按钮只在已填写 API 密钥且未在拉取中时可用。</summary>
        private void UpdateRefreshButtonState()
        {
            ModelRefreshButton.IsEnabled = !_isFetchingModels && !string.IsNullOrEmpty(ApiKeyBox.Password);
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateRefreshButtonState();
        }

        private async void BaseUrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await TryFetchCustomModelsAsync();
        }

        private async void ApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await TryFetchCustomModelsAsync();
        }

        private async void ModelCustomCombo_DropDownOpened(object sender, EventArgs e)
        {
            await TryFetchCustomModelsAsync();
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var preset = _currentPreset;
            var baseUrl = preset?.BaseUrl ?? BaseUrlBox.Text.Trim();

            string modelId;
            if (preset != null)
            {
                modelId = ModelCombo.SelectedItem as string ?? string.Empty;
                if (string.IsNullOrEmpty(modelId))
                {
                    ShowError("请先点击刷新按钮获取模型列表，再选择模型");
                    return;
                }
            }
            else
            {
                modelId = ModelCustomCombo.Text.Trim();
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

        /// <summary>服务商条目依次淡入上移，形成错峰入场。</summary>
        private void PlayProviderItemsStagger()
        {
            if (FindResource("ProviderItemsStagger") is Storyboard storyboard)
            {
                storyboard.Begin(this);
            }
        }

        /// <summary>表单字段依次淡入上移，形成错峰入场。</summary>
        private void PlayFormFieldsStagger()
        {
            if (FindResource("FormFieldsStagger") is Storyboard storyboard)
            {
                storyboard.Begin(this);
            }
        }
    }

    /// <summary>服务商预设：提供商名称、Base URL、API Key 申请页与 logo 资源路径（模型列表由 /models 动态拉取）。</summary>
    public class ModelPreset
    {
        public ModelPreset(string providerName, string baseUrl, string apiKeysPageUrl, string? logoPath)
        {
            ProviderName = providerName;
            BaseUrl = baseUrl;
            ApiKeysPageUrl = apiKeysPageUrl;
            LogoPath = logoPath;
        }

        public string ProviderName { get; }
        public string BaseUrl { get; }
        public string ApiKeysPageUrl { get; }
        public string? LogoPath { get; }
    }
}
