using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KfuPet.Models;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>
    /// 角色模型窗口：扫描 Characters 目录，以卡片列表展示各角色包
    /// （预览图、名称、作者与头像、描述），支持搜索、刷新与打开模型文件夹。
    /// </summary>
    public partial class CharacterGalleryWindow : Window
    {
        private readonly CharacterCatalogService _catalogService = new();

        private IReadOnlyList<CharacterPackageInfo> _packages = new List<CharacterPackageInfo>();
        private ICollectionView? _packageView;
        private bool _isLoading;

        public CharacterGalleryWindow()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                PlayEntranceAnimation();
                _ = ReloadAsync();
            };
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

        /// <summary>
        /// 重新扫描模型目录并刷新列表（首次加载与点击刷新共用）。
        /// </summary>
        private async Task ReloadAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            RefreshButton.IsEnabled = false;
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingText.Visibility = Visibility.Visible;
            PlayRefreshSpin();

            try
            {
                _packages = await _catalogService.LoadPackagesAsync();
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 模型列表加载失败：{ex.Message}");
                _packages = new List<CharacterPackageInfo>();
            }
            finally
            {
                _isLoading = false;
                RefreshButton.IsEnabled = true;
                StopRefreshSpin();
                LoadingText.Visibility = Visibility.Collapsed;
            }

            ModelList.ItemsSource = _packages;
            _packageView = CollectionViewSource.GetDefaultView(_packages);
            _packageView.Filter = FilterPackage;

            ApplyViewState();
            PlayItemsEntranceAnimation(stagger: true);
        }

        /// <summary>
        /// 搜索过滤：按名称、作者、标签匹配当前关键词。
        /// </summary>
        private bool FilterPackage(object item)
        {
            if (item is not CharacterPackageInfo package)
            {
                return false;
            }

            var keyword = SearchBox.Text.Trim();
            if (keyword.Length == 0)
            {
                return true;
            }

            return package.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || package.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || package.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholderText.Visibility = SearchBox.Text.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            _packageView?.Refresh();
            ApplyViewState();
            PlayItemsEntranceAnimation(stagger: false);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ReloadAsync();
        }

        /// <summary>
        /// 在资源管理器中打开模型目录，方便用户导入新角色包。
        /// </summary>
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var charactersDir = PromptService.FindCharactersDirectory();
            if (charactersDir == null)
            {
                MessageBox.Show(this, "没有找到 Characters 目录。", "角色模型",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"\"{charactersDir}\"");
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 打开模型文件夹失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 卡片“删除”：弹一次“是否删除”提醒，确认后删除角色包目录并刷新列表。
        /// </summary>
        private void DeleteCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CharacterPackageInfo package)
            {
                return;
            }

            var dialog = new DeleteCharacterDialog(package.Name);
            dialog.DeleteConfirmed += () => DeleteCharacterPackage(package);
            dialog.ShowDialog();
        }

        /// <summary>删除角色包目录（限定在 Characters 目录内），完成后重新扫描列表。</summary>
        private void DeleteCharacterPackage(CharacterPackageInfo package)
        {
            // 只允许删除 Characters 目录内的模型包，避免异常路径误删其他位置
            var charactersDir = PromptService.FindCharactersDirectory();
            var root = charactersDir == null
                ? string.Empty
                : Path.GetFullPath(charactersDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(package.DirectoryPath);
            if (root.Length == 0 || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning($"[角色] 拒绝删除模型包：目录不在 Characters 内（{package.DirectoryPath}）");
                MessageBox.Show(this, "该模型不在 Characters 目录内，已取消删除。", "角色模型",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Directory.Delete(target, recursive: true);
                Log.Info($"[角色] 已删除角色包：{package.Name}（{target}）");
            }
            catch (Exception ex)
            {
                Log.Error($"[角色] 角色包删除失败：{ex.Message}");
                MessageBox.Show(this, $"删除失败：{ex.Message}", "角色模型",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ = ReloadAsync();
        }

        /// <summary>
        /// 刷新数量文案与空状态：区分“一个模型都没有”和“搜索无结果”。
        /// </summary>
        private void ApplyViewState()
        {
            // 加载中由 LoadingText 单独表示状态，避免与空状态重叠
            if (_isLoading)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                return;
            }

            var total = _packages.Count;
            var invalidCount = _packages.Count(p => !p.IsValid);
            var shown = _packageView?.Cast<object>().Count() ?? total;

            CountText.Text = total switch
            {
                0 => "暂无模型",
                _ when invalidCount > 0 => $"共 {total} 个模型 · {invalidCount} 个无效",
                _ => $"共 {total} 个模型"
            };

            if (shown > 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                return;
            }

            EmptyStateTitle.Text = total == 0 ? "还没有角色模型" : "没有匹配的模型";
            EmptyStateHint.Text = total == 0
                ? "把角色文件夹放入 Characters 目录，并在文件夹中添加 character.json 即可显示。"
                : "换个关键词再试试吧。";
            EmptyState.Visibility = Visibility.Visible;
        }

        /// <summary>窗口打开时的淡入 + 轻微放大动画。</summary>
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

        /// <summary>
        /// 卡片逐个淡入上滑：重新扫描时错峰播放，搜索过滤重建的卡片不再错峰。
        /// 已经可见的卡片（动画早已完成）会跳过，避免重复播放。
        /// </summary>
        private void PlayItemsEntranceAnimation(bool stagger)
        {
            // 等容器生成后再播放动画
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 卡片容器还没生成时先强制布局，避免卡片停在透明状态
                if (ModelList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    ModelList.UpdateLayout();
                }

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                for (var i = 0; i < ModelList.Items.Count; i++)
                {
                    if (ModelList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                    {
                        continue;
                    }

                    var card = FindVisualChild<Border>(container);
                    if (card == null || card.Opacity >= 1)
                    {
                        continue;
                    }

                    var begin = stagger
                        ? TimeSpan.FromMilliseconds(80 + 40 * Math.Min(i, 12))
                        : TimeSpan.Zero;
                    var duration = TimeSpan.FromMilliseconds(260);

                    card.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });

                    // 模板里的变换实例已被冻结，需要换成新实例才能挂动画
                    var translate = new TranslateTransform();
                    card.RenderTransform = translate;
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(12, 0, duration) { BeginTime = begin, EasingFunction = ease });
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>刷新按钮图标持续旋转，表示正在扫描。</summary>
        private void PlayRefreshSpin()
        {
            if (RefreshIcon.RenderTransform is RotateTransform rotate)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
        }

        /// <summary>停止刷新按钮旋转并复位。</summary>
        private void StopRefreshSpin()
        {
            if (RefreshIcon.RenderTransform is RotateTransform rotate)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        /// <summary>在视觉树中向下查找第一个指定类型的子元素。</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}