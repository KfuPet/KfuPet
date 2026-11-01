using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KfuPet.Models;
using KfuPet.Services;

namespace KfuPet.Views
{
    /// <summary>模型下载窗口：按本机配置推荐三档模型，下载 GGUF 文件并写入部署记录。</summary>
    public partial class LocalModelDownloadWindow : Window
    {
        private readonly LocalModelDeploymentService _deployment = new();
        private readonly HardwareInfo _hardware;
        private string _modelDirectory = LocalModelDeploymentService.ResolveDirectory();
        private CancellationTokenSource? _downloadCts;
        private bool _downloading;

        public LocalModelDownloadWindow()
        {
            InitializeComponent();

            _hardware = HardwareProbe.Detect();
            HardwareGpuText.Text = _hardware.GpuName;
            HardwareVramText.Text = _hardware.HasDedicatedGpu
                ? $"独显显存 {_hardware.VramGb:0.0} GB"
                : "核显 / 无独显，CPU 推理";
            HardwareRamText.Text = $"{_hardware.RamGb:0.0} GB";
            HardwareCpuText.Text = $"{_hardware.LogicalCores} 线程";

            UpdateStoragePath();
            LoadModelOptions();
            Closing += OnWindowClosing;
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

        /// <summary>把当前模型存放目录显示到卡片上。</summary>
        private void UpdateStoragePath()
        {
            StoragePathText.Text = _modelDirectory;
            StoragePathText.ToolTip = _modelDirectory;
        }

        /// <summary>按硬件与已下载记录生成模型列表，并预选推荐项。</summary>
        private void LoadModelOptions()
        {
            var downloaded = _deployment.LoadAll();
            var options = LocalModelCatalog.Recommend(_hardware);

            foreach (var option in options)
            {
                option.IsDownloaded = downloaded.Any(m =>
                    string.Equals(m.FileName, option.FileName, StringComparison.OrdinalIgnoreCase));
            }

            ModelOptionList.ItemsSource = options;
            ModelOptionList.SelectedItem = options.FirstOrDefault(o => o.IsRecommended && !o.IsDownloaded)
                                           ?? options.FirstOrDefault(o => o.CanRun && !o.IsDownloaded)
                                           ?? options.FirstOrDefault(o => !o.IsDownloaded);
            UpdateSelectionText();
        }

        private void ModelOptionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionText();
        }

        private void UpdateSelectionText()
        {
            SelectionText.Text = ModelOptionList.SelectedItem is LocalModelOption option
                ? $"已选择：{option.Name}"
                : "尚未选择模型";
        }

        /// <summary>“更改目录”：选择模型存放位置并写入设置，随后按新目录刷新已下载状态。</summary>
        private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择本地模型存放目录",
                InitialDirectory = Directory.Exists(_modelDirectory)
                    ? _modelDirectory
                    : LocalModelDeploymentService.DefaultDirectory,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _modelDirectory = dialog.FolderName;
            SettingsService.Instance.SetLocalModelDirectory(_modelDirectory);
            UpdateStoragePath();
            LoadModelOptions();
        }

        /// <summary>下载按钮：未下载时开始下载；下载中再次点击则取消。</summary>
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                _downloadCts?.Cancel();
                return;
            }

            if (ModelOptionList.SelectedItem is not LocalModelOption option)
            {
                MessageBox.Show(this, "请先选择一个模型。", "模型下载",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (option.IsDownloaded)
            {
                MessageBox.Show(this, "该模型已经下载，无需重复下载。", "模型下载",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!option.CanRun)
            {
                var vramText = _hardware.HasDedicatedGpu ? $"显存 {_hardware.VramGb:0.0} GB" : "无独显";

                var proceed = MessageBox.Show(this,
                    $"当前选中的 {option.Name} 超出本机配置，可能无法加载或运行极慢。\n\n" +
                    $"模型要求：{option.RequirementText}\n" +
                    $"本机配置：{vramText} · 内存 {_hardware.RamGb:0.0} GB\n\n仍要继续吗？",
                    "配置不足",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (proceed != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var confirm = MessageBox.Show(this,
                $"确认下载 {option.Name}？\n\n下载体积约 {option.SizeText}，保存到：\n{_modelDirectory}",
                "确认下载",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            await DownloadAsync(option);
        }

        /// <summary>下载并写入部署记录：进度实时显示，失败按多个地址回退，取消时清理临时文件。</summary>
        private async Task DownloadAsync(LocalModelOption option)
        {
            _downloading = true;
            _downloadCts = new CancellationTokenSource();

            DownloadPanel.Visibility = Visibility.Visible;
            DownloadButtonText.Text = "取消下载";
            ModelOptionList.IsEnabled = false;
            ChooseFolderButton.IsEnabled = false;
            ProgressTitleText.Text = $"正在下载 {option.Name}";
            DownloadProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            ProgressDetailText.Text = "正在连接下载地址…";
            ProgressSpeedText.Text = string.Empty;

            var targetPath = _deployment.GetTargetPath(option, _modelDirectory);
            var progress = new Progress<DownloadProgress>(value =>
            {
                DownloadProgressBar.Value = value.Percent;
                ProgressPercentText.Text = $"{value.Percent:0}%";
                ProgressDetailText.Text = $"已下载 {value.ReceivedText} / {value.TotalText}";
                ProgressSpeedText.Text = $"{value.SpeedText} · {value.EtaText}";
            });

            try
            {
                var sha256 = await LocalModelDownloadService.DownloadAsync(
                    option, targetPath, progress, _downloadCts.Token);

                var deployed = new DeployedModel
                {
                    ModelName = option.Name,
                    FileName = option.FileName,
                    FilePath = targetPath,
                    SizeBytes = new FileInfo(targetPath).Length,
                    DeployedAt = DateTime.Now,
                    Sha256 = sha256,
                    Parameters = option.Parameters,
                    Quantization = option.Quantization
                };

                _deployment.Save(deployed);
                Log.Info($"[本地模型] 下载完成：{option.Name} → {targetPath}");

                // 先清掉下载状态，否则 Closing 会拦截由关闭窗口触发的退出
                _downloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;

                MessageBox.Show(this, $"{option.Name} 下载完成，已加入本地模型列表。", "模型下载",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (OperationCanceledException)
            {
                ResetDownloadUi();
                MessageBox.Show(this, "下载已取消，临时文件已清理。", "取消下载",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ResetDownloadUi();
                Log.Error($"[本地模型] 下载失败：{ex.Message}");
                MessageBox.Show(this, $"模型下载失败：\n{ex.Message}\n\n可以稍后重试。", "下载失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _downloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private void ResetDownloadUi()
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
            DownloadButtonText.Text = "下载并配置";
            ModelOptionList.IsEnabled = true;
            ChooseFolderButton.IsEnabled = true;
        }

        /// <summary>下载过程中禁止关闭窗口，先取消下载再退出。</summary>
        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_downloading)
            {
                e.Cancel = true;
                _downloadCts?.Cancel();
            }
        }
    }
}
