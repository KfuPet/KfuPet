using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KfuPet.Helpers;
using KfuPet.Models;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 设置窗口，左侧导航在通用、模型、模型功能、记忆、开发者模式、关于与存储占用之间切换。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>“关于”页版本徽章连续点击多少次后唤出开发者模式入口。</summary>
        private const int VersionBadgeRevealClickCount = 5;

        /// <summary>版本徽章相邻两次点击的最大间隔，超过则从 1 重新计数。</summary>
        private static readonly TimeSpan VersionBadgeClickInterval = TimeSpan.FromSeconds(2);

        private readonly MainWindow _mainWindow;
        private readonly UpdateService _updateService = new();
        private readonly StartupService _startupService = new();
        private bool _suppressToggleEvents;
        private bool _suppressModelToggleEvents;
        private bool _suppressDebugBonesEvents;
        private bool _isCheckingUpdate;
        private bool _isCurrentVersionExpanded;
        private bool _hasLoadedCurrentVersionInfo;
        private bool _isLoadingCurrentVersionInfo;
        private bool _suppressAppearanceEvents;
        private bool _suppressAutoStartEvents;
        private bool _suppressSavingEvents;
        private bool _suppressProactiveEvents;
        private bool _isLoadingStorageUsage;
        private bool _hasLoadedStorageUsage;
        private AddModelProviderDialog? _addModelProviderDialog;
        private int _versionBadgeClickCount;
        private DateTime _lastVersionBadgeClickTime;
        private DispatcherTimer? _toastHideTimer;

        private ModelConfigService ModelConfigService => _mainWindow.ModelConfigService;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();

            NavList.SelectedIndex = 0;
            LoadDeveloperState();
            LoadModels();
            RefreshStopWordsPreview();
            UpdateToolStatus();
            LoadSettingsIntoControls();

            VersionText.Text = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);

            // 直接用真实日志目录当提示，避免路径写死在文案里
            OpenLogButton.ToolTip = $"在资源管理器中打开日志目录：{LogFileWriter.LogDirectory}";

            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
            Closed += SettingsWindow_Closed;
            Activated += SettingsWindow_Activated;
            Log.Info("[窗口] 设置窗口已打开");
        }

        /// <summary>
        /// 把已保存的设置读入各控件：外观下拉菜单、开机自启动开关、节省消耗档位与主动搭话参数。
        /// 窗口构造时调用一次。
        /// </summary>
        private void LoadSettingsIntoControls()
        {
            // 外观下拉菜单：按当前主题偏好选中对应项（0 系统 / 1 浅色 / 2 深色）
            _suppressAppearanceEvents = true;
            AppearanceComboBox.SelectedIndex = Application.Current is App app
                ? app.ThemePreference switch { true => 2, false => 1, null => 0 }
                : 0;
            _suppressAppearanceEvents = false;

            // 开机自启动开关：状态直接读注册表，与系统实际设置保持一致
            _suppressAutoStartEvents = true;
            AutoStartToggle.IsChecked = _startupService.IsEnabled();
            _suppressAutoStartEvents = false;

            // 节省消耗档位：按已保存的设置选中对应滑块项，并同步自定义细分选项（0 AI 生成 / 1 本地文案）
            _suppressSavingEvents = true;
            (SettingsService.Instance.ModelSavingMode switch
            {
                SavingMode.Saving => SavingOnRadio,
                SavingMode.Custom => SavingCustomRadio,
                _ => SavingOffRadio
            }).IsChecked = true;
            TouchReactionSavingComboBox.SelectedIndex = SettingsService.Instance.CustomTouchReactionUseAi ? 0 : 1;
            UpdateNotificationSavingComboBox.SelectedIndex = SettingsService.Instance.CustomUpdateNotificationUseAi ? 0 : 1;
            SnapBackSavingComboBox.SelectedIndex = SettingsService.Instance.CustomSnapBackUseAi ? 0 : 1;
            ProactiveChatSavingComboBox.SelectedIndex = SettingsService.Instance.CustomProactiveChatUseAi ? 0 : 1;
            _suppressSavingEvents = false;
            UpdateCustomSavingOptionsVisibility();

            // 主动搭话：读取总开关与各项参数（控件事件在此期间忽略）
            _suppressProactiveEvents = true;
            ProactiveChatToggle.IsChecked = SettingsService.Instance.ProactiveChatEnabled;
            (SettingsService.Instance.ProactiveChatFrequency switch
            {
                ProactiveFrequency.Low => ProactiveLowRadio,
                ProactiveFrequency.High => ProactiveHighRadio,
                _ => ProactiveMediumRadio
            }).IsChecked = true;
            // 安静时段：归一化历史上"起止相同"的配置（现已不允许），再各自排除对方选项
            var quietStart = SettingsService.Instance.QuietHoursStart;
            var quietEnd = SettingsService.Instance.QuietHoursEnd;
            if (quietStart == quietEnd)
            {
                quietEnd = (quietStart + 1) % 24;
                SettingsService.Instance.SetQuietHours(quietStart, quietEnd);
            }

            RebuildQuietHourOptions(QuietHoursStartComboBox, quietEnd, quietStart);
            RebuildQuietHourOptions(QuietHoursEndComboBox, quietStart, quietEnd, markNextDayBeforeExcluded: true);
            QuietHoursToggle.IsChecked = SettingsService.Instance.QuietHoursEnabled;
            ProactiveIdleToggle.IsChecked = SettingsService.Instance.ProactiveIdleEnabled;
            ProactiveWelcomeToggle.IsChecked = SettingsService.Instance.ProactiveWelcomeBackEnabled;
            ProactiveGreetingToggle.IsChecked = SettingsService.Instance.ProactiveGreetingEnabled;
            _suppressProactiveEvents = false;
            UpdateProactiveOptionsVisibility();
            UpdateQuietHoursOptionsVisibility();
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            Log.Debug("[窗口] 设置窗口已关闭");
            _toastHideTimer?.Stop();
            _mainWindow.DeveloperModeService.EnabledChanged -= OnDeveloperModeChanged;
            _mainWindow.ToolStatusChanged -= OnToolStatusChanged;
            _mainWindow.SkeletonService.DebugSkeletonChanged -= OnDebugSkeletonChanged;
        }

        /// <summary>
        /// 窗口重新获得焦点时，若停在记忆页则从磁盘重载并刷新统计，
        /// 覆盖「删除后把备份文件复制回来」这类应用无法感知的外部文件变动。
        /// </summary>
        private void SettingsWindow_Activated(object? sender, EventArgs e)
        {
            if (NavList.SelectedIndex == 3)
            {
                RefreshMemoryStatistics();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 拖动标题栏移动窗口（无边框窗口无系统标题栏，需手动实现）。
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 窗口打开时的淡入 + 轻微放大动画。
        /// </summary>
        private void PlayEntranceAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            RootCard.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });

            if (RootCard.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            }
        }

        /// <summary>
        /// 切换到左侧导航的“模型配置”页。
        /// </summary>
        public void ShowModelConfigPage()
        {
            NavList.SelectedIndex = 1;
        }

        /// <summary>
        /// 把当前停用词以中文逗号拼接显示在卡片预览里。
        /// </summary>
        private void RefreshStopWordsPreview()
        {
            StopWordsPreviewText.Text = string.Join("，", _mainWindow.StopWordsService.Words);
        }

        /// <summary>
        /// 点击“编辑”打开停用词编辑对话框，确认后保存并刷新预览。
        /// </summary>
        private void EditStopWordsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EditStopWordsDialog();
            dialog.LoadWords(_mainWindow.StopWordsService.Words);
            dialog.StopWordsConfirmed += text =>
            {
                _mainWindow.StopWordsService.Save(ParseStopWords(text));
                RefreshStopWordsPreview();
            };
            dialog.Show();
        }

        /// <summary>把编辑框文本按中英文逗号或换行拆分、去空白、去空项。</summary>
        private static IReadOnlyList<string> ParseStopWords(string text)
        {
            return text
                .Split(new[] { '，', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => w.Length > 0)
                .ToList();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 选中底部“存储占用”入口时会清空功能导航的选中，此处不再处理
            if (NavList.SelectedIndex < 0) return;

            if (GeneralPanel == null || ModelConfigPanel == null || ModelFeaturesPanel == null ||
                MemoryPanel == null || DeveloperPanel == null || AboutPanel == null ||
                StoragePanel == null || StorageNavList == null) return;

            // 功能导航与底部“存储占用”入口互斥选中
            StorageNavList.SelectedIndex = -1;

            var currentPanel = NavList.SelectedIndex switch
            {
                1 => ModelConfigPanel,
                2 => ModelFeaturesPanel,
                3 => MemoryPanel,
                4 => DeveloperPanel,
                5 => AboutPanel,
                _ => GeneralPanel
            };
            ShowOnlyPanel(currentPanel);
            PlayPageEnterAnimation(currentPanel);

            if (NavList.SelectedIndex == 3)
            {
                PlayMemoryPageEntrance();
            }

            if (NavList.SelectedIndex == 5)
            {
                _ = LoadCurrentVersionInfoAsync();
            }
        }

        /// <summary>
        /// 底部“存储占用”入口选中：清空功能导航选中并切到存储占用页。
        /// 占用统计每个窗口会话只自动跑一次，重开设置窗口或点“刷新”时才会再统计。
        /// </summary>
        private void StorageNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StorageNavList.SelectedIndex < 0) return;

            NavList.SelectedIndex = -1;

            ShowOnlyPanel(StoragePanel);
            PlayPageEnterAnimation(StoragePanel);

            if (!_hasLoadedStorageUsage)
            {
                _ = LoadStorageUsageAsync();
            }
        }

        /// <summary>点击“刷新”：重新统计一次存储占用。</summary>
        private void StorageRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadStorageUsageAsync();
        }

        /// <summary>只显示指定页面，其余页面隐藏。</summary>
        private void ShowOnlyPanel(FrameworkElement target)
        {
            GeneralPanel.Visibility = ReferenceEquals(target, GeneralPanel) ? Visibility.Visible : Visibility.Collapsed;
            ModelConfigPanel.Visibility = ReferenceEquals(target, ModelConfigPanel) ? Visibility.Visible : Visibility.Collapsed;
            ModelFeaturesPanel.Visibility = ReferenceEquals(target, ModelFeaturesPanel) ? Visibility.Visible : Visibility.Collapsed;
            MemoryPanel.Visibility = ReferenceEquals(target, MemoryPanel) ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = ReferenceEquals(target, DeveloperPanel) ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = ReferenceEquals(target, AboutPanel) ? Visibility.Visible : Visibility.Collapsed;
            StoragePanel.Visibility = ReferenceEquals(target, StoragePanel) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 页面切换时，新页面从下方轻微滑入并淡入。
        /// </summary>
        private static void PlayPageEnterAnimation(FrameworkElement panel)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            panel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

            if (panel.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            }
        }

        /// <summary>从元素的 RenderTransform 中取出 TranslateTransform（兼容单变换与 TransformGroup）。</summary>
        private static TranslateTransform? FindTranslateTransform(FrameworkElement element)
        {
            return element.RenderTransform switch
            {
                TranslateTransform single => single,
                TransformGroup group => group.Children.OfType<TranslateTransform>().FirstOrDefault(),
                _ => null
            };
        }

        /// <summary>
        /// 外观下拉菜单选择变化：0 系统（跟随系统）/ 1 浅色 / 2 深色，带遮罩过渡动画。
        /// </summary>
        private void AppearanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAppearanceEvents)
            {
                return;
            }

            bool? preference = AppearanceComboBox.SelectedIndex switch
            {
                1 => false,
                2 => true,
                _ => null
            };
            PlayThemeTransition(preference);
        }

        /// <summary>
        /// 主题切换过渡：先用当前背景色遮罩盖住卡片，在遮罩下完成换色，再淡出露出新配色。
        /// 不能降低根卡片不透明度做过渡——本窗口背景透明，降低会直接透出桌面。
        /// </summary>
        private void PlayThemeTransition(bool? preference)
        {
            // 取出当前（旧主题）背景色并复制成静态画刷，避免资源替换后遮罩跟着变色
            if (FindResource("AppBackgroundBrush") is SolidColorBrush oldBrush)
            {
                ThemeTransitionOverlay.Background = new SolidColorBrush(oldBrush.Color);
            }

            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeIn.Completed += (s, _) =>
            {
                if (Application.Current is App app)
                {
                    app.SetThemePreference(preference);
                }

                ThemeTransitionOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            };
            ThemeTransitionOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// 开机自启动开关变化：写入 / 删除当前用户注册表 Run 项。
        /// 设置失败时回退到注册表实际状态，避免界面与系统不一致。
        /// </summary>
        private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressAutoStartEvents)
            {
                return;
            }

            if (!_startupService.SetEnabled(AutoStartToggle.IsChecked == true))
            {
                _suppressAutoStartEvents = true;
                AutoStartToggle.IsChecked = _startupService.IsEnabled();
                _suppressAutoStartEvents = false;
            }
        }

        /// <summary>
        /// 节省消耗档位变化：关闭 / 开启 / 自定义，保存档位、滑动滑块并刷新自定义细分选项。
        /// </summary>
        private void SavingModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSavingEvents)
            {
                return;
            }

            var mode = sender == SavingOnRadio ? SavingMode.Saving
                : sender == SavingCustomRadio ? SavingMode.Custom
                : SavingMode.Off;
            SettingsService.Instance.SetModelSavingMode(mode);
            UpdateSavingThumb(animate: true);
            UpdateCustomSavingOptionsVisibility();
        }

        /// <summary>滑块位置同步：播放滑动动画；首次布局或尺寸变化时直接落位。</summary>
        private void UpdateSavingThumb(bool animate)
        {
            var index = SavingCustomRadio.IsChecked == true ? 2 : SavingOnRadio.IsChecked == true ? 1 : 0;
            var translate = FindTranslateTransform(SavingModeThumb);
            if (translate == null || SavingModeThumb.ActualWidth <= 0)
            {
                return;
            }

            var target = index * SavingModeThumb.ActualWidth;
            if (animate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = target;
            }
        }

        /// <summary>滑块尺寸随窗口或 DPI 变化后重新落位，避免停在旧位置。</summary>
        private void SavingModeThumb_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSavingThumb(animate: false);
        }

        /// <summary>
        /// 自定义细分：触摸反应是否使用 AI 生成（0 AI 生成 / 1 角色台词）。
        /// </summary>
        private void TouchReactionSavingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSavingEvents)
            {
                return;
            }

            SettingsService.Instance.SetCustomTouchReactionUseAi(TouchReactionSavingComboBox.SelectedIndex == 0);
        }

        /// <summary>
        /// 自定义细分：更新通知文案是否使用 AI 生成（0 AI 生成 / 1 固定文案）。
        /// </summary>
        private void UpdateNotificationSavingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSavingEvents)
            {
                return;
            }

            SettingsService.Instance.SetCustomUpdateNotificationUseAi(UpdateNotificationSavingComboBox.SelectedIndex == 0);
        }

        /// <summary>
        /// 自定义细分：越界回正文案是否使用 AI 生成（0 AI 生成 / 1 角色台词）。
        /// </summary>
        private void SnapBackSavingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSavingEvents)
            {
                return;
            }

            SettingsService.Instance.SetCustomSnapBackUseAi(SnapBackSavingComboBox.SelectedIndex == 0);
        }

        /// <summary>
        /// 按当前档位刷新自定义细分选项的显隐：自定义模式展开（淡入 + 上滑），其余档位收起。
        /// </summary>
        private void UpdateCustomSavingOptionsVisibility()
        {
            if (SavingCustomRadio.IsChecked == true)
            {
                if (CustomSavingOptionsPanel.Visibility == Visibility.Visible)
                {
                    return;
                }

                CustomSavingOptionsPanel.Visibility = Visibility.Visible;

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                CustomSavingOptionsPanel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

                var translate = FindTranslateTransform(CustomSavingOptionsPanel);
                if (translate != null)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                }
            }
            else if (CustomSavingOptionsPanel.Visibility == Visibility.Visible)
            {
                CustomSavingOptionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 主动搭话总开关：保存并刷新参数区显隐。
        /// </summary>
        private void ProactiveChatToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            SettingsService.Instance.SetProactiveChatEnabled(ProactiveChatToggle.IsChecked == true);
            UpdateProactiveOptionsVisibility();
        }

        /// <summary>
        /// 按总开关刷新主动搭话参数区显隐：开启展开（淡入 + 上滑），关闭收起。
        /// </summary>
        private void UpdateProactiveOptionsVisibility()
        {
            if (ProactiveChatToggle.IsChecked == true)
            {
                if (ProactiveOptionsPanel.Visibility == Visibility.Visible)
                {
                    return;
                }

                ProactiveOptionsPanel.Visibility = Visibility.Visible;

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                ProactiveOptionsPanel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

                var translate = FindTranslateTransform(ProactiveOptionsPanel);
                if (translate != null)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                }
            }
            else if (ProactiveOptionsPanel.Visibility == Visibility.Visible)
            {
                ProactiveOptionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 主动搭话触发频率：低 / 中 / 高，保存档位并滑动滑块。
        /// </summary>
        private void ProactiveFrequencyRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            var frequency = sender == ProactiveLowRadio ? ProactiveFrequency.Low
                : sender == ProactiveHighRadio ? ProactiveFrequency.High
                : ProactiveFrequency.Medium;
            SettingsService.Instance.SetProactiveChatFrequency(frequency);
            UpdateProactiveFrequencyThumb(animate: true);
        }

        /// <summary>主动搭话频率滑块位置同步：播放滑动动画；首次布局或尺寸变化时直接落位。</summary>
        private void UpdateProactiveFrequencyThumb(bool animate)
        {
            var index = ProactiveHighRadio.IsChecked == true ? 2 : ProactiveMediumRadio.IsChecked == true ? 1 : 0;
            var translate = FindTranslateTransform(ProactiveFrequencyThumb);
            if (translate == null || ProactiveFrequencyThumb.ActualWidth <= 0)
            {
                return;
            }

            var target = index * ProactiveFrequencyThumb.ActualWidth;
            if (animate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = target;
            }
        }

        /// <summary>主动搭话频率滑块尺寸随窗口或 DPI 变化后重新落位，避免停在旧位置。</summary>
        private void ProactiveFrequencyThumb_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateProactiveFrequencyThumb(animate: false);
        }

        /// <summary>
        /// 重建安静时段下拉：候选项为 0–23 时，排除另一个下拉当前选中的小时（避免起止相同），
        /// 并用指定的小时恢复选中。markNextDayBeforeExcluded 为 true 时（结束下拉），
        /// 比排除小时更早的候选项会标注"次日"，表示该选择跨午夜。
        /// </summary>
        private static void RebuildQuietHourOptions(ComboBox comboBox, int excludedHour, int selectedHour, bool markNextDayBeforeExcluded = false)
        {
            comboBox.Items.Clear();
            for (var hour = 0; hour < 24; hour++)
            {
                if (hour == excludedHour)
                {
                    continue;
                }

                var isNextDay = markNextDayBeforeExcluded && hour < excludedHour;
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = BuildQuietHourOptionLabel(hour, isNextDay),
                    Tag = "\uE823"
                });
            }

            comboBox.SelectedIndex = selectedHour >= 0 ? FindQuietHourIndex(comboBox, selectedHour) : -1;
        }

        /// <summary>构造候选项显示：时间 + 缩小一号的 "(次日)" 标注（跨午夜时）。</summary>
        private static FrameworkElement BuildQuietHourOptionLabel(int hour, bool isNextDay)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"{hour:00}:00",
                VerticalAlignment = VerticalAlignment.Center
            });

            if (isNextDay)
            {
                var suffix = new TextBlock
                {
                    Text = "(次日)",
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                suffix.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
                panel.Children.Add(suffix);
            }

            return panel;
        }

        /// <summary>按小时值找下拉项下标；该小时被排除时返回 -1。标注"次日"的后缀不影响匹配。</summary>
        private static int FindQuietHourIndex(ComboBox comboBox, int hour)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && GetHourFromItem(item) == hour)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>从候选项内容（时间文本 + 可选 "(次日)" 标注）解析小时；解析失败返回 -1。</summary>
        private static int GetHourFromItem(ComboBoxItem item)
        {
            if (item.Content is StackPanel panel &&
                panel.Children.Count > 0 &&
                panel.Children[0] is TextBlock timeText &&
                int.TryParse(timeText.Text.Substring(0, 2), out var hour))
            {
                return hour;
            }

            return -1;
        }

        /// <summary>读取下拉当前选中的小时（忽略"次日"后缀）；未选择时返回 -1。</summary>
        private static int GetSelectedQuietHour(ComboBox comboBox)
        {
            return comboBox.SelectedItem is ComboBoxItem item ? GetHourFromItem(item) : -1;
        }

        /// <summary>
        /// 安静时段起止变化：保存两个小时值，并重建另一个下拉的候选项（排除刚选中的小时，避免起止相同）。
        /// </summary>
        private void QuietHoursComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            var startHour = GetSelectedQuietHour(QuietHoursStartComboBox);
            var endHour = GetSelectedQuietHour(QuietHoursEndComboBox);
            if (startHour < 0 || endHour < 0)
            {
                return;
            }

            SettingsService.Instance.SetQuietHours(startHour, endHour);

            // 只重建另一个下拉：它的候选项需要随本次选择变化（排除已选小时；结束列表还要标注跨午夜的"次日"）
            var suppress = _suppressProactiveEvents;
            _suppressProactiveEvents = true;
            if (sender == QuietHoursStartComboBox)
            {
                RebuildQuietHourOptions(QuietHoursEndComboBox, startHour, endHour, markNextDayBeforeExcluded: true);
            }
            else
            {
                RebuildQuietHourOptions(QuietHoursStartComboBox, endHour, startHour);
            }
            _suppressProactiveEvents = suppress;
        }

        /// <summary>安静时段开关：保存状态并刷新时间选择的显隐。</summary>
        private void QuietHoursToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            SettingsService.Instance.SetQuietHoursEnabled(QuietHoursToggle.IsChecked == true);
            UpdateQuietHoursOptionsVisibility();
        }

        /// <summary>
        /// 按开关刷新安静时段时间选择的显隐：开启展开（淡入 + 上滑），关闭收起。
        /// </summary>
        private void UpdateQuietHoursOptionsVisibility()
        {
            if (QuietHoursToggle.IsChecked == true)
            {
                if (QuietHoursOptionsPanel.Visibility == Visibility.Visible)
                {
                    return;
                }

                QuietHoursOptionsPanel.Visibility = Visibility.Visible;

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                QuietHoursOptionsPanel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

                var translate = FindTranslateTransform(QuietHoursOptionsPanel);
                if (translate != null)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                }
            }
            else if (QuietHoursOptionsPanel.Visibility == Visibility.Visible)
            {
                QuietHoursOptionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>独处搭话开关：保存状态。</summary>
        private void ProactiveIdleToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            SettingsService.Instance.SetProactiveIdleEnabled(ProactiveIdleToggle.IsChecked == true);
        }

        /// <summary>欢迎回来开关：保存状态。</summary>
        private void ProactiveWelcomeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            SettingsService.Instance.SetProactiveWelcomeBackEnabled(ProactiveWelcomeToggle.IsChecked == true);
        }

        /// <summary>定时问候开关：保存状态。</summary>
        private void ProactiveGreetingToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressProactiveEvents)
            {
                return;
            }

            SettingsService.Instance.SetProactiveGreetingEnabled(ProactiveGreetingToggle.IsChecked == true);
        }

        /// <summary>
        /// 自定义细分：主动搭话是否使用 AI 生成（0 AI 生成 / 1 角色台词）。
        /// </summary>
        private void ProactiveChatSavingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSavingEvents)
            {
                return;
            }

            SettingsService.Instance.SetCustomProactiveChatUseAi(ProactiveChatSavingComboBox.SelectedIndex == 0);
        }

        /// <summary>
        /// 记忆页入场：卡片依次错峰淡入上滑，统计数字滚动、进度条缓动填充。
        /// </summary>
        private void PlayMemoryPageEntrance()
        {
            var memory = _mainWindow.MemorySystem;

            // 先重载磁盘数据，避免删除后又把备份文件复制回来时，统计数字仍显示旧缓存
            memory.Reload();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var cards = new FrameworkElement[]
            {
                ShortMemoryCard, ArchiveMemoryCard, LongMemoryCard, ChatHistoryCard, LongTermMemoryCard, StopWordsCard
            };

            // 卡片错峰入场（每张比上一张晚 60ms）
            for (var i = 0; i < cards.Length; i++)
            {
                var begin = TimeSpan.FromMilliseconds(60 * i);
                var duration = TimeSpan.FromMilliseconds(260);

                cards[i].BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });

                var translate = FindTranslateTransform(cards[i]);
                if (translate != null)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(14, 0, duration) { BeginTime = begin, EasingFunction = ease });
                }
            }

            // 数字滚动 + 进度条填充，错开节奏更有层次
            PlayCountUpAnimation(ShortCountText, memory.ShortCount, TimeSpan.FromMilliseconds(120));
            PlayCountUpAnimation(ArchiveCountText, memory.ArchiveCount, TimeSpan.FromMilliseconds(180));
            PlayCountUpAnimation(LongCountText, memory.LongCount, TimeSpan.FromMilliseconds(240));

            PlayProgressAnimation(ShortProgressFill, memory.ShortCount, MemorySystem.ShortCapacity, TimeSpan.FromMilliseconds(150));
            PlayProgressAnimation(ArchiveProgressFill, memory.ArchiveCount, MemorySystem.ArchiveCapacity, TimeSpan.FromMilliseconds(210));
            PlayProgressAnimation(LongProgressFill, memory.LongCount, MemorySystem.LongCapacity, TimeSpan.FromMilliseconds(270));

            ShortLimitText.Text = $"/ {MemorySystem.ShortCapacity}";
            ArchiveLimitText.Text = $"/ {MemorySystem.ArchiveCapacity}";
            LongLimitText.Text = $"/ {MemorySystem.LongCapacity}";

            RefreshChatHistoryPreview();
            RefreshLongTermMemoryPreview();
        }

        /// <summary>把当前聊天记录数量显示在卡片预览里。</summary>
        private void RefreshChatHistoryPreview()
        {
            var count = _mainWindow.MemorySystem.GetChatHistory().Count;
            ChatHistoryPreviewText.Text = count > 0
                ? $"一共保存了 {count} 条对话，点“查看”可以回顾我们聊过的内容。"
                : "还没有聊天记录，去和我聊几句吧。";
        }

        /// <summary>把当前长期记忆数量显示在卡片预览里。</summary>
        private void RefreshLongTermMemoryPreview()
        {
            var count = _mainWindow.MemorySystem.LongCount;
            LongTermMemoryPreviewText.Text = count > 0
                ? $"我已经牢牢记住了 {count} 件关于你的事，点“查看”可以看到全部。"
                : "还没有长期记忆，多和我聊聊，我会慢慢记住关于你的事。";
        }

        /// <summary>从磁盘重载并直接刷新记忆页的统计数字、进度条与预览（不重播动画）。</summary>
        private void RefreshMemoryStatistics()
        {
            var memory = _mainWindow.MemorySystem;
            memory.Reload();

            ShortCountText.Text = memory.ShortCount.ToString();
            ArchiveCountText.Text = memory.ArchiveCount.ToString();
            LongCountText.Text = memory.LongCount.ToString();

            SetProgressFill(ShortProgressFill, memory.ShortCount, MemorySystem.ShortCapacity);
            SetProgressFill(ArchiveProgressFill, memory.ArchiveCount, MemorySystem.ArchiveCapacity);
            SetProgressFill(LongProgressFill, memory.LongCount, MemorySystem.LongCapacity);

            ShortLimitText.Text = $"/ {MemorySystem.ShortCapacity}";
            ArchiveLimitText.Text = $"/ {MemorySystem.ArchiveCapacity}";
            LongLimitText.Text = $"/ {MemorySystem.LongCapacity}";

            RefreshChatHistoryPreview();
            RefreshLongTermMemoryPreview();
        }

        /// <summary>清除进行中的动画后，把进度条直接设置为当前占比。</summary>
        private static void SetProgressFill(ScaleTransform fill, int count, int capacity)
        {
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            fill.ScaleX = capacity > 0 ? Math.Clamp((double)count / capacity, 0, 1) : 0;
        }

        /// <summary>点击“查看”打开聊天记录窗口。</summary>
        private void ViewChatHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new ChatHistoryWindow(_mainWindow.MemorySystem.GetChatHistory());
            window.Show();
        }

        /// <summary>点击“查看”打开长期记忆窗口。</summary>
        private void ViewLongTermMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new LongTermMemoryWindow(_mainWindow.MemorySystem.GetLongTermMemories());
            window.Show();
        }

        /// <summary>
        /// 点击“缓存清理”：弹出勾选对话框，确认后清理所选资源（历史日志、软件配置、记忆）。
        /// </summary>
        private void CleanupCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CleanupCacheDialog();
            dialog.CleanupConfirmed += kinds =>
            {
                var cleanedParts = new List<string>();

                if (kinds.HasFlag(CleanupCacheDialog.CacheKinds.Logs))
                {
                    // 正在写入的那一份日志保留，只删历史日志
                    var currentLogPath = (Application.Current as App)?.CurrentLogFilePath;
                    var deletedCount = CacheCleanupService.DeleteLogFiles(currentLogPath);
                    cleanedParts.Add($"日志 {deletedCount} 份");
                }

                if (kinds.HasFlag(CleanupCacheDialog.CacheKinds.Config))
                {
                    CacheCleanupService.DeleteConfigFiles();
                    ResetSoftwareConfigValues();
                    cleanedParts.Add("软件配置");
                }

                if (kinds.HasFlag(CleanupCacheDialog.CacheKinds.Memory))
                {
                    var memory = _mainWindow.MemorySystem;
                    memory.ClearShortTerm();
                    memory.ClearArchive();
                    memory.ClearLongTerm();
                    cleanedParts.Add("记忆");
                }

                // 软件配置复位后，运行中的界面与实时状态必然和新配置不一致，直接重启软件最干净
                if (kinds.HasFlag(CleanupCacheDialog.CacheKinds.Config))
                {
                    Log.Info($"[清理] 缓存清理完成：{string.Join("、", cleanedParts)}，3 秒后自动重启软件");
                    ScheduleRestart(cleanedParts);
                    return;
                }

                ShowToast($"缓存清理完成：{string.Join("、", cleanedParts)}");

                // 重新统计占用，让页面上的数字滚动到清理后的大小
                _ = LoadStorageUsageAsync();
            };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 软件配置清理后的值复位：内存中的设置、模型与停用词回到初始值。
        /// 只做值复位——等待重启的这段时间里，任何一次保存都会把当前值写回配置文件，
        /// 若不复位就会把旧值写回去；界面与实时状态（主题、开发者模式等）交给重启自然恢复。
        /// </summary>
        private void ResetSoftwareConfigValues()
        {
            SettingsService.Instance.ResetToDefaults();
            ModelConfigService.ResetToDefaults();
            _mainWindow.StopWordsService.ResetToDefaults();

            Log.Info("[清理] 软件配置已复位为初始值，等待重启生效");
        }

        /// <summary>提示清理结果并在 3 秒后自动重启软件（等待期间禁用清理按钮，避免重复触发）。</summary>
        private void ScheduleRestart(IReadOnlyList<string> cleanedParts)
        {
            CleanupCacheButton.IsEnabled = false;
            ShowToast($"已清理：{string.Join("、", cleanedParts)}，3 秒后自动重启软件…");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RelaunchAndExit();
            };
            timer.Start();
        }

        /// <summary>
        /// 拉起新的软件实例并退出当前进程。
        /// 新实例带重启参数：启动时会先等本进程释放单实例锁，避免被单实例检查当成多开而退出。
        /// </summary>
        private static void RelaunchAndExit()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Log.Warning("[清理] 未能获取程序路径，自动重启失败，请手动重新打开软件");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exePath, App.RestartArgument) { UseShellExecute = true });
                Log.Info("[清理] 已拉起新的软件实例，当前进程退出以完成重启");
            }
            catch (Exception ex)
            {
                // 拉起失败时保持当前进程存活，避免用户既没重启成功又丢了软件
                Log.Error($"[清理] 自动重启失败：{ex.Message}");
                return;
            }

            Application.Current.Shutdown();
        }

        /// <summary>模型包卡片“前往清理”：打开角色模型窗口。</summary>
        private void OpenCharacterGalleryButton_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.OpenCharacterGalleryWindow();
        }

        /// <summary>统计数字从 0 滚动到目标整数值（TextBlock 没有可动画的数字属性，用定时器驱动插值）。</summary>
        private static void PlayCountUpAnimation(TextBlock text, int target, TimeSpan beginTime)
            => PlayCountUpAnimation(text, target, beginTime, value => ((long)Math.Round(value)).ToString());

        /// <summary>
        /// 统计数字从 0 滚动到目标值，每帧显示文本由格式化函数决定（用于字节数等带单位的场景）。
        /// </summary>
        private static void PlayCountUpAnimation(TextBlock text, double target, TimeSpan beginTime, Func<double, string> format)
        {
            var start = DateTime.UtcNow + beginTime;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                var elapsed = DateTime.UtcNow - start;
                if (elapsed < TimeSpan.Zero)
                {
                    return;
                }

                var t = Math.Min(1.0, elapsed.TotalMilliseconds / 500.0);
                // Cubic EaseOut
                var eased = 1 - Math.Pow(1 - t, 3);
                text.Text = format(target * eased);

                if (t >= 1.0)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        /// <summary>进度条从 0 缓动填充到当前数量占比。</summary>
        private static void PlayProgressAnimation(ScaleTransform fill, int count, int capacity, TimeSpan beginTime)
            => PlayRatioAnimation(fill, capacity > 0 ? Math.Clamp((double)count / capacity, 0, 1) : 0, beginTime);

        /// <summary>进度条从 0 缓动填充到指定比例（0~1）。</summary>
        private static void PlayRatioAnimation(ScaleTransform fill, double ratio, TimeSpan beginTime)
        {
            fill.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, Math.Clamp(ratio, 0, 1), TimeSpan.FromMilliseconds(600))
                {
                    BeginTime = beginTime,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        /// <summary>
        /// 统计并展示存储占用（软件本体、缓存目录、模型包）。
        /// 目录遍历在后台线程完成，不阻塞界面；统计期间刷新按钮禁用并旋转，避免重复统计。
        /// </summary>
        private async Task LoadStorageUsageAsync()
        {
            if (_isLoadingStorageUsage) return;

            _isLoadingStorageUsage = true;
            StorageRefreshButton.IsEnabled = false;
            PlayStorageRefreshSpin();
            try
            {
                StorageInstallSizeText.Text = "统计中…";
                StorageCacheSizeText.Text = "统计中…";
                StorageModelsSizeText.Text = "统计中…";

                var usage = await StorageUsageService.LoadUsageAsync();
                ApplyStorageUsage(usage);
            }
            catch (Exception ex)
            {
                Log.Warning($"[存储] 占用统计失败：{ex.Message}");
                StorageInstallSizeText.Text = "统计失败";
                StorageCacheSizeText.Text = "统计失败";
                StorageModelsSizeText.Text = "统计失败";
            }
            finally
            {
                _isLoadingStorageUsage = false;
                _hasLoadedStorageUsage = true;
                StorageRefreshButton.IsEnabled = true;
                StopStorageRefreshSpin();
            }
        }

        /// <summary>把统计结果填入存储占用页。</summary>
        private void ApplyStorageUsage(StorageUsageInfo usage)
        {
            // 模型包：合计占用；多于一个时再展示占用最大的 3 个
            StorageModelsCountText.Text = usage.ModelsPath.Length > 0
                ? $"共 {usage.Packages.Count} 个模型包"
                : "未找到模型包目录";

            FillStoragePackageRows(usage);

            PlayStoragePageEntrance(usage);
        }

        /// <summary>刷新按钮图标持续旋转，表示正在统计占用。</summary>
        private void PlayStorageRefreshSpin()
        {
            if (StorageRefreshIcon.RenderTransform is RotateTransform rotate)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
        }

        /// <summary>停止刷新按钮旋转并复位。</summary>
        private void StopStorageRefreshSpin()
        {
            if (StorageRefreshIcon.RenderTransform is RotateTransform rotate)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        /// <summary>填入模型包明细行（最多 3 行，不足的行隐藏）；只有一个模型包时不展示明细。</summary>
        private void FillStoragePackageRows(StorageUsageInfo usage)
        {
            StoragePackagesCard.Visibility = usage.Packages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            var rows = StoragePackageRowControls();
            var top = usage.Packages.Take(rows.Length).ToList();
            for (var i = 0; i < rows.Length; i++)
            {
                var visible = i < top.Count;
                rows[i].Row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) continue;

                rows[i].Name.Text = top[i].Name;
                rows[i].Size.Text = StorageUsageService.FormatBytes(top[i].Bytes);
            }
        }

        /// <summary>取出页面上预置的 3 行模型包明细控件（行容器、名称、大小、占比条）。</summary>
        private (StackPanel Row, TextBlock Name, TextBlock Size, ScaleTransform Fill)[] StoragePackageRowControls()
        {
            return new (StackPanel Row, TextBlock Name, TextBlock Size, ScaleTransform Fill)[]
            {
                (StoragePackageRow1, StoragePackageRow1Name, StoragePackageRow1Size, StoragePackageRow1Fill),
                (StoragePackageRow2, StoragePackageRow2Name, StoragePackageRow2Size, StoragePackageRow2Fill),
                (StoragePackageRow3, StoragePackageRow3Name, StoragePackageRow3Size, StoragePackageRow3Fill)
            };
        }

        /// <summary>
        /// 存储占用页入场：卡片错峰淡入上滑，数字从 0 滚动到实际大小，
        /// 明细行占比条以占用最大的模型包为满格缓动填充。
        /// </summary>
        private void PlayStoragePageEntrance(StorageUsageInfo usage)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var cards = new List<FrameworkElement> { StorageInstallCard, StorageCacheCard, StorageModelsCard };
            if (StoragePackagesCard.Visibility == Visibility.Visible)
            {
                cards.Add(StoragePackagesCard);
            }

            // 卡片错峰入场（每张比上一张晚 60ms）
            for (var i = 0; i < cards.Count; i++)
            {
                var begin = TimeSpan.FromMilliseconds(60 * i);
                var duration = TimeSpan.FromMilliseconds(260);

                cards[i].BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });

                var translate = FindTranslateTransform(cards[i]);
                if (translate != null)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(14, 0, duration) { BeginTime = begin, EasingFunction = ease });
                }
            }

            // 数字滚动：字节数按人类可读单位逐帧换算
            PlayCountUpAnimation(StorageInstallSizeText, usage.InstallBytes, TimeSpan.FromMilliseconds(120),
                value => StorageUsageService.FormatBytes((long)Math.Round(value)));
            PlayCountUpAnimation(StorageCacheSizeText, usage.CacheBytes, TimeSpan.FromMilliseconds(180),
                value => StorageUsageService.FormatBytes((long)Math.Round(value)));
            PlayCountUpAnimation(StorageModelsSizeText, usage.ModelsBytes, TimeSpan.FromMilliseconds(240),
                value => StorageUsageService.FormatBytes((long)Math.Round(value)));

            // 明细行占比条：以占用最大的模型包为满格，其余按比例
            var rows = StoragePackageRowControls();
            var top = usage.Packages.Take(rows.Length).ToList();
            var maxBytes = top.Count > 0 ? top[0].Bytes : 0;
            for (var i = 0; i < top.Count; i++)
            {
                var ratio = maxBytes > 0 ? (double)top[i].Bytes / maxBytes : 0;
                PlayRatioAnimation(rows[i].Fill, ratio, TimeSpan.FromMilliseconds(160 + 60 * i));
            }
        }

        /// <summary>
        /// 添加模型按钮点击：以非模态方式打开添加窗口（先选服务商，再在表单页填写配置），
        /// 确认后把新模型插入列表。已存在窗口时不再新建，而是把它带到前台提醒。
        /// </summary>
        private void AddModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_addModelProviderDialog != null)
            {
                _addModelProviderDialog.FlashToFront();
                return;
            }

            _addModelProviderDialog = new AddModelProviderDialog();
            _addModelProviderDialog.ModelConfirmed += (s, args) =>
            {
                var model = ModelConfigService.Add(args.BaseUrl, args.ApiKey, args.ModelName, args.ModelId);
                AddModelCard(model);
            };
            _addModelProviderDialog.Closed += (s, args) => _addModelProviderDialog = null;
            _addModelProviderDialog.Show();
        }

        /// <summary>
        /// 加载已保存的模型配置并重建列表。
        /// </summary>
        private void LoadModels()
        {
            ModelList.Items.Clear();
            foreach (var model in ModelConfigService.Models)
            {
                AddModelCard(model);
            }
            UpdateModelListVisibility();
        }

        /// <summary>
        /// 向列表添加一个模型卡片，并绑定删除按钮与开关事件。
        /// </summary>
        private void AddModelCard(ModelConfig model)
        {
            var item = new ListBoxItem
            {
                Content = model.ModelName,
                Tag = model,
                RenderTransform = new TranslateTransform()
            };

            // 等模板应用后再找删除按钮与开关并挂事件
            item.Loaded += (s, e) =>
            {
                if (item.Template.FindName("EditModelButton", item) is Button editButton)
                {
                    editButton.Click += (s2, e2) => EditModelCard(item);
                }

                if (item.Template.FindName("DeleteModelButton", item) is Button deleteButton)
                {
                    deleteButton.Click += (s2, e2) => RemoveModelCard(item);
                }

                if (item.Template.FindName("ModelToggle", item) is ToggleButton toggle)
                {
                    toggle.IsChecked = model.IsActive;
                    toggle.Checked += (s2, e2) => OnModelToggleChanged(item, true);
                    toggle.Unchecked += (s2, e2) => OnModelToggleChanged(item, false);
                }
            };

            ModelList.Items.Add(item);
            UpdateModelListVisibility();

            // 入场动画
            item.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            if (item.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        /// <summary>
        /// 模型开关切换：勾选表示设为当前使用，取消勾选表示不选中任何模型。
        /// </summary>
        private void OnModelToggleChanged(ListBoxItem item, bool isChecked)
        {
            if (_suppressModelToggleEvents) return;

            if (item.Tag is ModelConfig model)
            {
                ModelConfigService.SetActiveModel(isChecked ? model.Id : null);
                RefreshModelToggles();
            }
        }

        /// <summary>
        /// 依据服务端最新状态刷新所有卡片的开关显示。
        /// </summary>
        private void RefreshModelToggles()
        {
            _suppressModelToggleEvents = true;
            foreach (var item in ModelList.Items.OfType<ListBoxItem>())
            {
                if (item.Tag is ModelConfig model &&
                    item.Template.FindName("ModelToggle", item) is ToggleButton toggle)
                {
                    toggle.IsChecked = model.IsActive;
                }
            }
            _suppressModelToggleEvents = false;
        }

        /// <summary>
        /// 删除模型：先弹确认对话框（确认按钮倒计时 3 秒后才可点），确认后再移除卡片。
        /// </summary>
        private void RemoveModelCard(ListBoxItem item)
        {
            if (item.Tag is not ModelConfig model)
            {
                return;
            }

            var dialog = new DeleteModelDialog(model.ModelName);
            dialog.DeleteConfirmed += () => RemoveModelCardConfirmed(item, model);
            dialog.ShowDialog();
        }

        /// <summary>
        /// 确认删除后执行：移除模型配置，卡片淡出后从列表移除。
        /// </summary>
        private void RemoveModelCardConfirmed(ListBoxItem item, ModelConfig model)
        {
            ModelConfigService.Remove(model.Id);

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                ModelList.Items.Remove(item);
                UpdateModelListVisibility();
            };
            item.BeginAnimation(OpacityProperty, fadeOut);

            if (item.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            }
        }

        /// <summary>
        /// 打开编辑对话框，预填该模型配置，确认后更新并刷新卡片显示。
        /// </summary>
        private void EditModelCard(ListBoxItem item)
        {
            if (item.Tag is not ModelConfig model) return;

            var dialog = new AddModelDialog(model);
            dialog.ModelConfirmed += (s, args) =>
            {
                ModelConfigService.Update(model.Id, args.BaseUrl, args.ApiKey, args.ModelName, args.ModelId);
                item.Content = args.ModelName;
            };
            dialog.Show();
        }

        /// <summary>
        /// 根据列表是否为空切换空状态提示的显示。
        /// </summary>
        private void UpdateModelListVisibility()
        {
            ModelEmptyText.Visibility = ModelList.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LoadDeveloperState()
        {
            _mainWindow.DeveloperModeService.EnabledChanged += OnDeveloperModeChanged;
            _mainWindow.ToolStatusChanged += OnToolStatusChanged;
            _mainWindow.SkeletonService.DebugSkeletonChanged += OnDebugSkeletonChanged;

            _suppressToggleEvents = true;
            DeveloperModeToggle.IsChecked = _mainWindow.DeveloperModeService.IsEnabled;
            _suppressToggleEvents = false;

            UpdateDebugBonesPanel();
            UpdateDeveloperNavVisibility();
        }

        private void OnDeveloperModeChanged(object? sender, EventArgs e)
        {
            _suppressToggleEvents = true;
            DeveloperModeToggle.IsChecked = _mainWindow.DeveloperModeService.IsEnabled;
            _suppressToggleEvents = false;

            // 关闭开发者模式时，一并关闭调试线框，避免线框残留显示
            if (!_mainWindow.DeveloperModeService.IsEnabled)
            {
                _mainWindow.SkeletonService.SetDebugSkeleton(false);
            }

            UpdateDebugBonesPanel();
        }

        /// <summary>
        /// 同步“开发者模式”导航入口的显隐：仅当开发者模式处于开启状态时显示。
        /// 关闭开关后不立即隐藏，等设置窗口关闭、下次再打开时按开关状态重新判定；
        /// 入口平时默认隐藏，只能通过“关于”页版本徽章连续点击 5 次唤出。
        /// </summary>
        private void UpdateDeveloperNavVisibility()
        {
            DeveloperNavItem.Visibility = _mainWindow.DeveloperModeService.IsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 显示左侧导航的“开发者模式”入口
        /// </summary>
        private void ShowDeveloperNavItem()
        {
            if (DeveloperNavItem.Visibility == Visibility.Visible)
            {
                return;
            }

            DeveloperNavItem.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(240);
            DeveloperNavItem.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            if (DeveloperNavItem.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-10, 0, duration) { EasingFunction = ease });
            }
        }

        /// <summary>
        /// 在页面下方居中弹出飘窗提示
        /// 重复调用会重置停留计时，不会叠加多个提示。
        /// </summary>
        private void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastCard.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(220);
            ToastCard.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, duration) { EasingFunction = ease });

            _toastHideTimer ??= CreateToastHideTimer();
            _toastHideTimer.Stop();
            _toastHideTimer.Start();
        }

        /// <summary>创建飘窗自动隐藏计时器，到时淡出并折叠。</summary>
        private DispatcherTimer CreateToastHideTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                HideToast();
            };
            return timer;
        }

        /// <summary>飘窗淡出动画结束后折叠，避免残留元素挡住下方内容。</summary>
        private void HideToast()
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (s, e) => ToastCard.Visibility = Visibility.Collapsed;
            ToastCard.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// 开发者模式开启时显示“骨骼调试线框”开关，关闭时隐藏。
        /// 显隐带淡入淡出 + 上下滑动缓动，避免生硬跳变。
        /// </summary>
        private void UpdateDebugBonesPanel()
        {
            var enabled = _mainWindow.DeveloperModeService.IsEnabled;

            if (enabled)
            {
                DebugBonesPanel.Visibility = Visibility.Visible;
                PlayDebugBonesPanelAnimation(true);
            }
            else if (DebugBonesPanel.Visibility == Visibility.Visible)
            {
                PlayDebugBonesPanelAnimation(false, () =>
                {
                    DebugBonesPanel.Visibility = Visibility.Collapsed;
                });
            }

            _suppressDebugBonesEvents = true;
            DebugBonesToggle.IsChecked = _mainWindow.SkeletonService.ShowDebugSkeleton;
            _suppressDebugBonesEvents = false;
        }

        /// <summary>
        /// 播放“骨骼调试线框”卡片的显隐动画：淡入淡出 + 轻微上下滑动。
        /// </summary>
        private void PlayDebugBonesPanelAnimation(bool showing, Action? completed = null)
        {
            var ease = new CubicEase { EasingMode = showing ? EasingMode.EaseOut : EasingMode.EaseIn };
            var duration = TimeSpan.FromMilliseconds(220);

            var fade = new DoubleAnimation(showing ? 0 : 1, showing ? 1 : 0, duration)
            {
                EasingFunction = ease
            };
            if (completed != null)
            {
                fade.Completed += (s, e) => completed();
            }
            DebugBonesPanel.BeginAnimation(OpacityProperty, fade);

            var translate = FindTranslateTransform(DebugBonesPanel);
            if (translate != null)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(showing ? -10 : 0, showing ? 0 : -10, duration)
                    {
                        EasingFunction = ease
                    });
            }
        }

        /// <summary>
        /// 骨骼调试线框状态变化（可能来自开发者工具 IPC）时同步开关显示。
        /// </summary>
        private void OnDebugSkeletonChanged(object? sender, EventArgs e)
        {
            _suppressDebugBonesEvents = true;
            DebugBonesToggle.IsChecked = _mainWindow.SkeletonService.ShowDebugSkeleton;
            _suppressDebugBonesEvents = false;
        }

        private void DebugBonesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressDebugBonesEvents) return;
            _mainWindow.SkeletonService.SetDebugSkeleton(DebugBonesToggle.IsChecked == true);
        }

        private void OnToolStatusChanged(object? sender, EventArgs e)
        {
            UpdateToolStatus();
        }

        private void DeveloperModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressToggleEvents) return;
            _mainWindow.DeveloperModeService.SetEnabled(DeveloperModeToggle.IsChecked == true);
        }

        /// <summary>
        /// 刷新开发者工具状态文案：优先按真实管道连接状态显示；
        /// 进程在跑但未连接时给出中间提示；完全没启动时再提示去启动。
        /// </summary>
        private void UpdateToolStatus()
        {
            if (_mainWindow.IsToolConnected)
            {
                ToolStatusText.Text = "开发者工具已经连上我啦，随时欢迎来研究～";
            }
            else if (_mainWindow.IsToolRunning)
            {
                ToolStatusText.Text = "开发者工具启动了，但还没连上我，看看开发者模式开关有没有打开～";
            }
            else
            {
                ToolStatusText.Text = "开发者工具还没连过来，想研究我的话记得启动它。";
            }
        }

        /// <summary>
        /// “关于”页版本徽章点击：2 秒内连续点击 5 次唤出开发者模式入口，并默认开启开发者模式。
        /// </summary>
        private void VersionBadge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var now = DateTime.UtcNow;
            if (now - _lastVersionBadgeClickTime > VersionBadgeClickInterval)
            {
                _versionBadgeClickCount = 0;
            }

            _lastVersionBadgeClickTime = now;
            _versionBadgeClickCount++;

            if (_versionBadgeClickCount < VersionBadgeRevealClickCount)
            {
                return;
            }

            _versionBadgeClickCount = 0;
            Log.Info("[开发者模式] 关于页版本徽章连续点击达标，显示开发者入口并默认开启开发者模式");
            ShowDeveloperNavItem();
            _mainWindow.DeveloperModeService.SetEnabled(true);
            ShowToast("开发者模式已开启，入口已显示在左侧啦～");
        }

        /// <summary>
        /// 关于页“检查更新”按钮点击。
        /// </summary>
        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdate)
            {
                return;
            }

            _isCheckingUpdate = true;
            try
            {
                await CheckForUpdatesAsync();
            }
            finally
            {
                _isCheckingUpdate = false;
            }
        }

        /// <summary>
        /// 检查更新：已是最新时提示，有新版本时先询问用户，确认后拉起更新程序并退出桌宠。
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            var result = await _updateService.CheckAsync();

            if (result == null)
            {
                var failDialog = new UpdateDialog(false, "未知", "未知", null)
                {
                    Owner = this
                };
                failDialog.TitleText.Text = "检查更新";
                failDialog.StatusIcon.Text = "\uE783"; // 警告图标
                failDialog.StatusTitleText.Text = "检查更新失败";
                failDialog.StatusDetailText.Text = "请检查网络后重试";
                failDialog.ConfirmButton.Content = "确定";
                failDialog.ShowDialog();
                return;
            }

            UpdatePrompt.Show(this, result);

            // 弹窗关闭后（如用户选择“取消”），把区块同步为本次检查结果，保证“关于新版本”内容一致
            if (result.IsUpdateAvailable)
            {
                await ApplyNewVersionInfoAsync(result);
                _hasLoadedCurrentVersionInfo = true;
            }
        }

        /// <summary>
        /// 关于页“打开日志”按钮点击：在资源管理器中打开日志目录，并选中本次运行的日志文件。
        /// </summary>
        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LogFileWriter.LogDirectory);

                var currentLogFile = (Application.Current as App)?.CurrentLogFilePath;
                if (!string.IsNullOrEmpty(currentLogFile) && File.Exists(currentLogFile))
                {
                    // 选中本次运行的日志文件，省得用户自己在目录里翻
                    Process.Start("explorer.exe", $"/select,\"{currentLogFile}\"");
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = LogFileWriter.LogDirectory,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[日志] 打开日志目录失败：{ex.Message}");
                MessageBox.Show($"打开日志目录失败：{ex.Message}", "KfuPet", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 关于页“关于此版本”头部点击：展开或折叠更新内容，
        /// 箭头回弹旋转，内容区淡入下滑 / 淡出上滑。
        /// </summary>
        private void AboutCurrentVersionHeader_Click(object sender, MouseButtonEventArgs e)
        {
            AnimateHeaderScale(1);

            _isCurrentVersionExpanded = !_isCurrentVersionExpanded;

            var chevronAnimation = new DoubleAnimation(_isCurrentVersionExpanded ? 90 : 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            AboutCurrentVersionChevronRotate.BeginAnimation(RotateTransform.AngleProperty, chevronAnimation);

            if (_isCurrentVersionExpanded)
            {
                AboutCurrentVersionContent.Visibility = Visibility.Visible;
                AboutCurrentVersionContent.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                AboutCurrentVersionContentSlide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(280))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                AboutCurrentVersionContent.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                var slideUp = new DoubleAnimation(-10, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                slideUp.Completed += (s, _) =>
                {
                    // 动画结束后再真正折叠，避免内容突然消失
                    if (!_isCurrentVersionExpanded)
                    {
                        AboutCurrentVersionContent.Visibility = Visibility.Collapsed;
                    }
                };
                AboutCurrentVersionContentSlide.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        /// <summary>
        /// 头部悬停：高亮层淡入，箭头提亮。
        /// </summary>
        private void AboutCurrentVersionHeader_MouseEnter(object sender, MouseEventArgs e)
        {
            AboutCurrentVersionHoverOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            AboutCurrentVersionChevron.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimaryBrush");
        }

        /// <summary>
        /// 头部离开：高亮层淡出，箭头恢复，同时复位按压缩放。
        /// </summary>
        private void AboutCurrentVersionHeader_MouseLeave(object sender, MouseEventArgs e)
        {
            AboutCurrentVersionHoverOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            AboutCurrentVersionChevron.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondaryBrush");
            AnimateHeaderScale(1);
        }

        /// <summary>
        /// 头部按下：轻微缩小，模拟按压手感。
        /// </summary>
        private void AboutCurrentVersionHeader_MouseDown(object sender, MouseButtonEventArgs e)
        {
            AnimateHeaderScale(0.97);
        }

        /// <summary>
        /// 头部按压/释放缩放：按下快速缩小，松开带回弹恢复。
        /// </summary>
        private void AnimateHeaderScale(double to)
        {
            var pressed = to < 1;
            IEasingFunction easing = pressed
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
            var duration = TimeSpan.FromMilliseconds(pressed ? 110 : 220);

            AboutCurrentVersionHeaderScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(to, duration) { EasingFunction = easing });
            AboutCurrentVersionHeaderScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(to, duration) { EasingFunction = easing });
        }

        /// <summary>
        /// 拉取版本发布信息，填充“关于此版本 / 关于新版本”区块：
        /// 检测到新版本时切换为“关于新版本”并展示新版本信息，否则展示本机版本信息。
        /// 只在关于页首次显示时加载一次。
        /// </summary>
        private async Task LoadCurrentVersionInfoAsync()
        {
            if (_hasLoadedCurrentVersionInfo || _isLoadingCurrentVersionInfo)
            {
                return;
            }

            _isLoadingCurrentVersionInfo = true;
            try
            {
                // 先检测是否有新版本：有则区块切换为“关于新版本”，内容同步为新版本的发布信息
                var checkResult = await _updateService.CheckAsync();
                if (checkResult is { IsUpdateAvailable: true })
                {
                    await ApplyNewVersionInfoAsync(checkResult);
                    _hasLoadedCurrentVersionInfo = true;
                    return;
                }

                var versionText = $"v{_updateService.CurrentVersion.ToString(3)}";

                var release = await _updateService.GetCurrentReleaseAsync();
                if (release == null)
                {
                    await ApplyVersionInfoAsync("关于此版本", versionText, string.Empty, "暂时获取不到此版本的更新说明，请检查网络后重试。");
                    return;
                }

                var dateText = release.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                var notesText = string.IsNullOrWhiteSpace(release.ReleaseNotes)
                    ? "该版本没有提供更新说明。"
                    : release.ReleaseNotes.Trim();
                await ApplyVersionInfoAsync("关于此版本", versionText, dateText, notesText);
                _hasLoadedCurrentVersionInfo = true;
            }
            finally
            {
                _isLoadingCurrentVersionInfo = false;
            }
        }

        /// <summary>
        /// 按检查更新结果同步“关于新版本”区块：标题、版本徽章、发布日期与更新说明均取新版本的信息。
        /// </summary>
        private async Task ApplyNewVersionInfoAsync(UpdateCheckResult result)
        {
            var versionText = $"v{result.LatestVersion.ToString(3)}";
            var dateText = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            var notesText = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                ? "该版本没有提供更新说明。"
                : result.ReleaseNotes.Trim();
            await ApplyVersionInfoAsync("关于新版本", versionText, dateText, notesText);
        }

        /// <summary>
        /// 版本信息入场：先淡出占位内容，替换标题与文本后再淡入，
        /// 徽章带回弹放大，日期从左侧滑入。
        /// </summary>
        private async Task ApplyVersionInfoAsync(string titleText, string badgeText, string dateText, string notesText)
        {
            var fadeOutDuration = TimeSpan.FromMilliseconds(140);
            var fadeOut = new DoubleAnimation(0, fadeOutDuration);
            var fadeOutCompleted = new TaskCompletionSource();
            fadeOut.Completed += (s, _) => fadeOutCompleted.SetResult();
            CurrentVersionBadgeBorder.BeginAnimation(OpacityProperty, fadeOut);
            CurrentReleaseDateText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fadeOutDuration));
            AboutVersionTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fadeOutDuration));
            await fadeOutCompleted.Task;

            AboutVersionTitle.Text = titleText;
            CurrentVersionBadge.Text = badgeText;
            CurrentReleaseDateText.Text = dateText;
            MarkdownRenderer.Render(notesText, CurrentReleaseNotesPanel);

            var fadeInDuration = TimeSpan.FromMilliseconds(220);
            var fadeInEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
            CurrentVersionBadgeBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, fadeInDuration) { EasingFunction = fadeInEasing });
            CurrentReleaseDateText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, fadeInDuration) { EasingFunction = fadeInEasing });
            AboutVersionTitle.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, fadeInDuration) { EasingFunction = fadeInEasing });
            CurrentReleaseDateSlide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-6, 0, fadeInDuration) { EasingFunction = fadeInEasing });

            var popDuration = TimeSpan.FromMilliseconds(320);
            var popEasing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            CurrentVersionBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, 1, popDuration) { EasingFunction = popEasing });
            CurrentVersionBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, 1, popDuration) { EasingFunction = popEasing });
        }
    }
}
