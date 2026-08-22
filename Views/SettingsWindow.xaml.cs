using System.Windows;
using System.Windows.Controls;

namespace KfuPet.Views
{
    /// <summary>
    /// 设置窗口，左侧导航在“模型配置”与“开发者模式”之间切换。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _suppressToggleEvents;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();

            NavList.SelectedIndex = 0;
            LoadDeveloperState();
            UpdateToolStatus();

            Closed += SettingsWindow_Closed;
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _mainWindow.DeveloperModeService.EnabledChanged -= OnDeveloperModeChanged;
            _mainWindow.ToolRunningChanged -= OnToolRunningChanged;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelConfigPanel == null || DeveloperPanel == null) return;

            var showModel = NavList.SelectedIndex == 0;
            ModelConfigPanel.Visibility = showModel ? Visibility.Visible : Visibility.Collapsed;
            DeveloperPanel.Visibility = showModel ? Visibility.Collapsed : Visibility.Visible;
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
                ? "开发者工具：已启动"
                : "开发者工具：未启动";
        }
    }
}
