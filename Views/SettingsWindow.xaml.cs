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
        private bool _isCheckingUpdate;

        private ModelConfigService ModelConfigService => _mainWindow.ModelConfigService;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();

            NavList.SelectedIndex = 0;
            LoadDeveloperState();
            LoadModels();
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

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelConfigPanel == null || DeveloperPanel == null || AboutPanel == null) return;

            ModelConfigPanel.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            var currentPanel = NavList.SelectedIndex switch
            {
                1 => DeveloperPanel,
                2 => AboutPanel,
                _ => ModelConfigPanel
            };
            PlayPageEnterAnimation(currentPanel);
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

        /// <summary>
        /// 添加模型按钮点击：以非模态方式打开配置窗口，确认后把新模型插入列表。
        /// </summary>
        private void AddModelButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddModelDialog();
            dialog.ModelConfirmed += (s, args) =>
            {
                var model = ModelConfigService.Add(args.Provider, args.BaseUrl, args.ApiKey, args.ModelName);
                AddModelCard(model);
            };
            dialog.Show();
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

            _suppressToggleEvents = true;
            DeveloperModeToggle.IsChecked = _mainWindow.DeveloperModeService.IsEnabled;
            _suppressToggleEvents = false;
        }

        private void OnDeveloperModeChanged(object? sender, EventArgs e)
        {
            _suppressToggleEvents = true;
            DeveloperModeToggle.IsChecked = _mainWindow.DeveloperModeService.IsEnabled;
            _suppressToggleEvents = false;
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
