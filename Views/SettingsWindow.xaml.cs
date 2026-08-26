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
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _mainWindow.DeveloperModeService.EnabledChanged -= OnDeveloperModeChanged;
            _mainWindow.ToolRunningChanged -= OnToolRunningChanged;
            _mainWindow.SkeletonService.DebugSkeletonChanged -= OnDebugSkeletonChanged;
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
            NavList.SelectedIndex = 0;
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
            if (ModelConfigPanel == null || MemoryPanel == null || DeveloperPanel == null || AboutPanel == null) return;

            ModelConfigPanel.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            MemoryPanel.Visibility = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = NavList.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

            var currentPanel = NavList.SelectedIndex switch
            {
                1 => MemoryPanel,
                2 => DeveloperPanel,
                3 => AboutPanel,
                _ => ModelConfigPanel
            };
            PlayPageEnterAnimation(currentPanel);

            if (NavList.SelectedIndex == 1)
            {
                PlayMemoryPageEntrance();
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
        /// 记忆页入场：卡片依次错峰淡入上滑，统计数字滚动、进度条缓动填充。
        /// </summary>
        private void PlayMemoryPageEntrance()
        {
            var memory = _mainWindow.MemorySystem;
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
        /// 从列表移除模型卡片，带淡出动画。
        /// </summary>
        private void RemoveModelCard(ListBoxItem item)
        {
            if (item.Tag is ModelConfig model)
            {
                ModelConfigService.Remove(model.Id);
            }

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
