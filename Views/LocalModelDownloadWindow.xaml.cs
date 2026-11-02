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
        /// <summary>下载前磁盘空间检查预留的余量（256 MB），避免把磁盘写满。</summary>
        private const long DiskSafetyMarginBytes = 256L * 1024 * 1024;

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
            Log.Info($"[本地模型] 打开下载窗口：{_hardware.GpuName} · 独显显存 {_hardware.VramGb:0.0} GB · " +
                     $"内存 {_hardware.RamGb:0.0} GB · {_hardware.LogicalCores} 线程；存放目录 {_modelDirectory}");

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

            Log.Debug($"[本地模型] 可下载模型刷新：已下载 {downloaded.Count} 个；" +
                      $"推荐 {options.FirstOrDefault(o => o.IsRecommended)?.Name ?? "无"}");
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
            Log.Info($"[本地模型] 存放目录已更改：{_modelDirectory}");
            UpdateStoragePath();
            LoadModelOptions();
        }

        /// <summary>下载按钮：未下载时开始下载；下载中再次点击则取消。</summary>
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                Log.Info("[本地模型] 用户请求取消下载");
                _downloadCts?.Cancel();
                return;
            }

            if (ModelOptionList.SelectedItem is not LocalModelOption option)
            {
                ShowMessage("模型下载", "请先选择一个模型。", MessageDialogKind.Info, "知道了");
                return;
            }

            if (option.IsDownloaded)
            {
                ShowMessage("模型下载", "该模型已经下载，无需重复下载。", MessageDialogKind.Info, "知道了");
                return;
            }

            if (!option.CanRun)
            {
                var vramText = _hardware.HasDedicatedGpu ? $"显存 {_hardware.VramGb:0.0} GB" : "无独显";
                var dialog = CreateDialog(
                    "配置不足",
                    $"当前选中的 {option.Name} 超出本机配置，可能无法加载或运行极慢。\n\n" +
                    $"模型要求：{option.RequirementText}\n" +
                    $"本机配置：{vramText} · 内存 {_hardware.RamGb:0.0} GB",
                    MessageDialogKind.Warning,
                    "仍要继续",
                    showCancel: true);

                dialog.Confirmed += () => ShowDownloadConfirm(option);
                dialog.Show();
                return;
            }

            ShowDownloadConfirm(option);
        }

        /// <summary>确认下载：说明体积与保存目录，确认后开始下载。</summary>
        private void ShowDownloadConfirm(LocalModelOption option)
        {
            var dialog = CreateDialog(
                "确认下载",
                $"确认下载 {option.Name}？\n\n下载体积约 {option.SizeText}，保存到：\n{_modelDirectory}",
                MessageDialogKind.Question,
                "开始下载",
                showCancel: true);

            dialog.Confirmed += () => _ = DownloadAsync(option);
            dialog.Show();
        }

        /// <summary>创建一个归属本窗口的提示弹窗；非模态显示，避免禁用桌宠窗口。</summary>
        private MessageDialog CreateDialog(
            string title,
            string message,
            MessageDialogKind kind,
            string confirmText,
            bool showCancel = false,
            string? cancelText = null)
        {
            var dialog = new MessageDialog(title, message, kind, confirmText, showCancel, cancelText);
            if (IsVisible)
            {
                dialog.Owner = this;
            }

            return dialog;
        }

        /// <summary>显示只有确认按钮的提示弹窗。</summary>
        private void ShowMessage(string title, string message, MessageDialogKind kind, string confirmText)
        {
            CreateDialog(title, message, kind, confirmText).Show();
        }

        /// <summary>下载并写入部署记录：空间预检、断点续传、SHA256 校验，失败按多个地址回退。</summary>
        private async Task DownloadAsync(LocalModelOption option)
        {
            var targetPath = _deployment.GetTargetPath(option, _modelDirectory);
            var partPath = LocalModelDownloadService.GetPartPath(targetPath);
            var resumedBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

            // 先检查剩余空间，不够就直接提示，避免下到一半才失败
            var requiredBytes = LocalModelDownloadService.GetRemainingBytes(option, targetPath);
            var availableBytes = LocalModelDownloadService.GetAvailableBytes(targetPath);
            if (availableBytes >= 0 && availableBytes < requiredBytes + DiskSafetyMarginBytes)
            {
                Log.Warning($"[本地模型] 磁盘空间不足：还需要 {requiredBytes} 字节，剩余 {availableBytes} 字节");
                ShowMessage(
                    "磁盘空间不足",
                    $"下载 {option.Name} 还需要约 {FormatSpace(requiredBytes)}，" +
                    $"但保存目录所在磁盘仅剩 {FormatSpace(availableBytes)}。\n\n" +
                    "请清理磁盘空间或更换存放目录后重试。",
                    MessageDialogKind.Warning,
                    "知道了");
                return;
            }

            _downloading = true;
            _downloadCts = new CancellationTokenSource();

            DownloadPanel.Visibility = Visibility.Visible;
            DownloadButtonText.Text = "取消下载";
            ModelOptionList.IsEnabled = false;
            ChooseFolderButton.IsEnabled = false;
            ProgressTitleText.Text = $"正在下载 {option.Name}";
            DownloadProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            ProgressDetailText.Text = resumedBytes > 0
                ? $"检测到未完成的下载，从 {FormatSpace(resumedBytes)} 处继续…"
                : "正在连接下载地址…";
            ProgressSpeedText.Text = string.Empty;

            Log.Info($"[本地模型] 开始下载：{option.Name}（{option.SizeText}）→ {targetPath}" +
                     (resumedBytes > 0 ? $"，断点 {FormatSpace(resumedBytes)}" : string.Empty));
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
                Log.Info($"[本地模型] 下载完成：{option.Name}（{deployed.SizeText}）→ {targetPath}");

                // 先清掉下载状态，否则 Closing 会拦截由关闭窗口触发的退出
                _downloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;

                // 恢复按钮状态并刷新已下载标记，避免用户关掉提示后重复下载同一个模型
                ResetDownloadUi();
                LoadModelOptions();

                var completedDialog = CreateDialog(
                    "模型下载",
                    $"{option.Name} 下载完成，已加入本地模型列表。",
                    MessageDialogKind.Success,
                    "知道了");
                completedDialog.Confirmed += Close;
                completedDialog.Show();
            }
            catch (OperationCanceledException)
            {
                ResetDownloadUi();
                ShowMessage("取消下载", "下载已取消，断点已保存，下次下载会从中断处继续。",
                    MessageDialogKind.Info, "知道了");
            }
            catch (Exception ex)
            {
                ResetDownloadUi();
                Log.Error($"[本地模型] 下载失败：{ex.Message}");
                var hint = File.Exists(partPath)
                    ? "已保留下载进度，重试时会自动续传。"
                    : "可以稍后重试。";
                ShowMessage("下载失败", $"模型下载失败：\n{ex.Message}\n\n{hint}",
                    MessageDialogKind.Error, "知道了");
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

        /// <summary>把字节数格式化为空间提示文本（GB / MB）。</summary>
        private static string FormatSpace(long bytes)
        {
            return bytes >= 1024L * 1024 * 1024
                ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
                : $"{bytes / 1024d / 1024d:0.0} MB";
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
