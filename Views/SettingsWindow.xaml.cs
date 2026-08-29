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
using KfuPet.Models;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 设置窗口，左侧导航在“模型配置”、“开发者模式”与“关于”之间切换。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly UpdateService _updateService = new();
        private bool _suppressToggleEvents;
        private bool _suppressModelToggleEvents;
        private bool _suppressDebugBonesEvents;
        private bool _isCheckingUpdate;
        private bool _isNewVersionExpanded;
        private bool _hasLoadedNewVersionInfo;
        private bool _isLoadingNewVersionInfo;
        private bool _suppressAppearanceEvents;
        private AddModelDialog? _addModelDialog;

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

            VersionText.Text = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);

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
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _mainWindow.DeveloperModeService.EnabledChanged -= OnDeveloperModeChanged;
            _mainWindow.ToolRunningChanged -= OnToolRunningChanged;
            _mainWindow.SkeletonService.DebugSkeletonChanged -= OnDebugSkeletonChanged;
        }

        /// <summary>
        /// 窗口重新获得焦点时，若停在记忆页则从磁盘重载并刷新统计，
        /// 覆盖「删除后把备份文件复制回来」这类应用无法感知的外部文件变动。
        /// </summary>
        private void SettingsWindow_Activated(object? sender, EventArgs e)
        {
            if (NavList.SelectedIndex == 2)
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
            if (GeneralPanel == null || ModelConfigPanel == null || MemoryPanel == null || DeveloperPanel == null || AboutPanel == null) return;

            GeneralPanel.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            ModelConfigPanel.Visibility = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            MemoryPanel.Visibility = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = NavList.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = NavList.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            var currentPanel = NavList.SelectedIndex switch
            {
                1 => ModelConfigPanel,
                2 => MemoryPanel,
                3 => DeveloperPanel,
                4 => AboutPanel,
                _ => GeneralPanel
            };
            PlayPageEnterAnimation(currentPanel);

            if (NavList.SelectedIndex == 2)
            {
                PlayMemoryPageEntrance();
            }

            if (NavList.SelectedIndex == 4)
            {
                _ = LoadNewVersionInfoAsync();
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
        /// 添加模型按钮点击：以非模态方式打开配置窗口，确认后把新模型插入列表。
        /// 已存在添加窗口时不再新建，而是把它带到前台并短暂置顶提醒。
        /// </summary>
        private void AddModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_addModelDialog != null)
            {
                _addModelDialog.FlashToFront();
                return;
            }

            _addModelDialog = new AddModelDialog();
            _addModelDialog.ModelConfirmed += (s, args) =>
            {
                var model = ModelConfigService.Add(args.BaseUrl, args.ApiKey, args.ModelName, args.ModelId);
                AddModelCard(model);
            };
            _addModelDialog.Closed += (s, args) => _addModelDialog = null;
            _addModelDialog.Show();
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
            _mainWindow.ToolRunningChanged += OnToolRunningChanged;
            _mainWindow.SkeletonService.DebugSkeletonChanged += OnDebugSkeletonChanged;

            _suppressToggleEvents = true;
            DeveloperModeToggle.IsChecked = _mainWindow.DeveloperModeService.IsEnabled;
            _suppressToggleEvents = false;

            UpdateDebugBonesPanel();
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

        private void OnToolRunningChanged(object? sender, EventArgs e)
        {
            UpdateToolStatus();
        }

        private void DeveloperModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressToggleEvents) return;
            _mainWindow.DeveloperModeService.SetEnabled(DeveloperModeToggle.IsChecked == true);
        }

        private void UpdateToolStatus()
        {
            ToolStatusText.Text = _mainWindow.IsToolRunning
                ? "开发者工具已经连上我啦，随时欢迎来研究～"
                : "开发者工具还没连过来，想研究我的话记得启动它。";
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
        /// 检查更新：已是最新时提示，有新版本时先询问用户，确认后用默认浏览器打开发布页面。
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

            var dialog = new UpdateDialog(
                result.IsUpdateAvailable,
                result.CurrentVersion.ToString(3),
                result.LatestVersion.ToString(3),
                result.ReleaseNotes)
            {
                Owner = this
            };

            dialog.DownloadConfirmed += (s, e) => OpenReleasePage(result.ReleasePageUrl);
            dialog.ShowDialog();
        }

        /// <summary>
        /// 关于页“关于新版本”头部点击：展开或折叠更新内容，
        /// 箭头回弹旋转，内容区淡入下滑 / 淡出上滑。
        /// </summary>
        private void AboutNewVersionHeader_Click(object sender, MouseButtonEventArgs e)
        {
            AnimateHeaderScale(1);

            _isNewVersionExpanded = !_isNewVersionExpanded;

            var chevronAnimation = new DoubleAnimation(_isNewVersionExpanded ? 90 : 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            AboutNewVersionChevronRotate.BeginAnimation(RotateTransform.AngleProperty, chevronAnimation);

            if (_isNewVersionExpanded)
            {
                AboutNewVersionContent.Visibility = Visibility.Visible;
                AboutNewVersionContent.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                AboutNewVersionContentSlide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(280))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                AboutNewVersionContent.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                var slideUp = new DoubleAnimation(-10, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                slideUp.Completed += (s, _) =>
                {
                    // 动画结束后再真正折叠，避免内容突然消失
                    if (!_isNewVersionExpanded)
                    {
                        AboutNewVersionContent.Visibility = Visibility.Collapsed;
                    }
                };
                AboutNewVersionContentSlide.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        /// <summary>
        /// 头部悬停：高亮层淡入，箭头提亮。
        /// </summary>
        private void AboutNewVersionHeader_MouseEnter(object sender, MouseEventArgs e)
        {
            AboutNewVersionHoverOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            AboutNewVersionChevron.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimaryBrush");
        }

        /// <summary>
        /// 头部离开：高亮层淡出，箭头恢复，同时复位按压缩放。
        /// </summary>
        private void AboutNewVersionHeader_MouseLeave(object sender, MouseEventArgs e)
        {
            AboutNewVersionHoverOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            AboutNewVersionChevron.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondaryBrush");
            AnimateHeaderScale(1);
        }

        /// <summary>
        /// 头部按下：轻微缩小，模拟按压手感。
        /// </summary>
        private void AboutNewVersionHeader_MouseDown(object sender, MouseButtonEventArgs e)
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

            AboutNewVersionHeaderScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(to, duration) { EasingFunction = easing });
            AboutNewVersionHeaderScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(to, duration) { EasingFunction = easing });
        }

        /// <summary>
        /// 从 GitHub 拉取最新版本信息，填充“关于新版本”区块。只在关于页首次显示时加载一次。
        /// </summary>
        private async Task LoadNewVersionInfoAsync()
        {
            if (_hasLoadedNewVersionInfo || _isLoadingNewVersionInfo)
            {
                return;
            }

            _isLoadingNewVersionInfo = true;
            try
            {
                var result = await _updateService.CheckAsync();
                if (result == null)
                {
                    await ApplyVersionInfoAsync("获取失败", string.Empty, "无法获取更新信息，请检查网络后重试。");
                    return;
                }

                var dateText = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                var notesText = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? "该版本没有提供更新说明。"
                    : result.ReleaseNotes.Trim();
                await ApplyVersionInfoAsync($"v{result.LatestVersion.ToString(3)}", dateText, notesText);
                _hasLoadedNewVersionInfo = true;
            }
            finally
            {
                _isLoadingNewVersionInfo = false;
            }
        }

        /// <summary>
        /// 版本信息入场：先淡出占位内容，替换文本后再淡入，
        /// 徽章带回弹放大，日期从左侧滑入。
        /// </summary>
        private async Task ApplyVersionInfoAsync(string badgeText, string dateText, string notesText)
        {
            var fadeOutDuration = TimeSpan.FromMilliseconds(140);
            var fadeOut = new DoubleAnimation(0, fadeOutDuration);
            var fadeOutCompleted = new TaskCompletionSource();
            fadeOut.Completed += (s, _) => fadeOutCompleted.SetResult();
            LatestVersionBadgeBorder.BeginAnimation(OpacityProperty, fadeOut);
            LatestReleaseDateText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fadeOutDuration));
            await fadeOutCompleted.Task;

            LatestVersionBadge.Text = badgeText;
            LatestReleaseDateText.Text = dateText;
            LatestReleaseNotesText.Text = notesText;

            var fadeInDuration = TimeSpan.FromMilliseconds(220);
            var fadeInEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
            LatestVersionBadgeBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, fadeInDuration) { EasingFunction = fadeInEasing });
            LatestReleaseDateText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, fadeInDuration) { EasingFunction = fadeInEasing });
            LatestReleaseDateSlide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-6, 0, fadeInDuration) { EasingFunction = fadeInEasing });

            var popDuration = TimeSpan.FromMilliseconds(320);
            var popEasing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            LatestVersionBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, 1, popDuration) { EasingFunction = popEasing });
            LatestVersionBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, 1, popDuration) { EasingFunction = popEasing });
        }

        /// <summary>
        /// 用系统默认浏览器打开发布页面。
        /// </summary>
        private static void OpenReleasePage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show($"打开下载页面失败，请手动访问：\n{url}",
                    "KfuPet", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
