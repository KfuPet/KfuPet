using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KfuPet.Models;
using Microsoft.Win32;

namespace KfuPet.Views
{
    /// <summary>
    /// 编辑“允许模型打开的应用”白名单：通过挑选 .exe 添加条目，可改名、可移除。
    /// 确认时回传完整列表，由调用方负责持久化。
    /// </summary>
    public partial class EditAllowedAppsDialog : Window
    {
        private readonly ObservableCollection<AllowedAppEntry> _apps = new();

        /// <summary>确认保存时触发，携带编辑后的白名单。</summary>
        public event Action<IReadOnlyList<AllowedAppEntry>>? AllowedAppsConfirmed;

        public EditAllowedAppsDialog()
        {
            InitializeComponent();
            AppListControl.ItemsSource = _apps;
            _apps.CollectionChanged += (s, e) => UpdateEmptyHint();
            UpdateEmptyHint();
            Loaded += (s, e) => PlayEntranceAnimation();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        /// <summary>用现有白名单预填列表（复制副本，不修改调用方传入的对象）。</summary>
        public void Load(IReadOnlyList<AllowedAppEntry> apps)
        {
            _apps.Clear();
            foreach (var app in apps)
            {
                _apps.Add(new AllowedAppEntry { Name = app.Name, ExecutablePath = app.ExecutablePath });
            }

            UpdateEmptyHint();
        }

        /// <summary>挑选一个或多个 .exe 加入列表；同一路径只保留一条，默认名称取文件名（可改）。</summary>
        private void AddAppButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要允许打开的应用",
                Filter = "应用程序 (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            foreach (var file in dialog.FileNames)
            {
                if (_apps.Any(a => string.Equals(a.ExecutablePath, file, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _apps.Add(new AllowedAppEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    ExecutablePath = file
                });
            }
        }

        private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: AllowedAppEntry entry })
            {
                _apps.Remove(entry);
            }
        }

        /// <summary>列表为空时显示引导文案。</summary>
        private void UpdateEmptyHint()
        {
            EmptyHintText.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // 去掉名称两端空白、丢弃空名称，并按路径去重后回传
            var result = _apps
                .Select(a => new AllowedAppEntry { Name = a.Name.Trim(), ExecutablePath = a.ExecutablePath.Trim() })
                .Where(a => a.Name.Length > 0 && a.ExecutablePath.Length > 0)
                .GroupBy(a => a.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            AllowedAppsConfirmed?.Invoke(result);
            Close();
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
}
