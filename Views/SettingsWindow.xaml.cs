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
    /// 设置窗口，左侧导航在“模型配置”、“开发者模式”与“关于”之间切换。
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

            // 节省消耗档位：按已保存的设置选中对应滑块项，并同步自定义细分选项（0 AI 生成 / 1 内置文案）
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
            _suppressSavingEvents = false;
            UpdateCustomSavingOptionsVisibility();

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
            if (GeneralPanel == null || ModelConfigPanel == null || ModelFeaturesPanel == null ||
                MemoryPanel == null || DeveloperPanel == null || AboutPanel == null) return;

            GeneralPanel.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            ModelConfigPanel.Visibility = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            ModelFeaturesPanel.Visibility = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            MemoryPanel.Visibility = NavList.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = NavList.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = NavList.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

            var currentPanel = NavList.SelectedIndex switch
            {
                1 => ModelConfigPanel,
                2 => ModelFeaturesPanel,
                3 => MemoryPanel,
                4 => DeveloperPanel,
                5 => AboutPanel,
                _ => GeneralPanel
            };
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
        /// 自定义细分：触摸反应是否使用 AI 生成（0 AI 生成 / 1 内置文案）。
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
        /// 自定义细分：更新通知文案是否使用 AI 生成（0 AI 生成 / 1 内置文案）。
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
        /// 自定义细分：越界回正文案是否使用 AI 生成（0 AI 生成 / 1 内置文案）。
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
        /// 点击“删除记忆”：弹出勾选对话框，确认后按选择清空对应记忆并刷新统计卡片。
        /// </summary>
        private void DeleteMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DeleteMemoryDialog();
            dialog.DeleteConfirmed += kinds =>
            {
                var memory = _mainWindow.MemorySystem;
                if (kinds.HasFlag(DeleteMemoryDialog.MemoryKinds.ShortTerm))
                {
                    memory.ClearShortTerm();
                }
                if (kinds.HasFlag(DeleteMemoryDialog.MemoryKinds.Archive))
                {
                    memory.ClearArchive();
                }
                if (kinds.HasFlag(DeleteMemoryDialog.MemoryKinds.LongTerm))
                {
                    memory.ClearLongTerm();
                }

                // 重新播放入场动画，让清零后的数字与进度条重新滚动
                PlayMemoryPageEntrance();
            };
            dialog.ShowDialog();
        }

        /// <summary>统计数字从 0 滚动到目标值（TextBlock 没有可动画的数字属性，用定时器驱动插值）。</summary>
        private static void PlayCountUpAnimation(TextBlock text, int target, TimeSpan beginTime)
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
                text.Text = ((int)Math.Round(target * eased)).ToString();

                if (t >= 1.0)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        /// <summary>进度条从 0 缓动填充到当前占比。</summary>
        private static void PlayProgressAnimation(ScaleTransform fill, int count, int capacity, TimeSpan beginTime)
        {
            var ratio = capacity > 0 ? Math.Clamp((double)count / capacity, 0, 1) : 0;
            fill.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, ratio, TimeSpan.FromMilliseconds(600))
                {
                    BeginTime = beginTime,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
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
