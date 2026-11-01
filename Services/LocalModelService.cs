using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 本地模型运行协调器：只负责用户手动启用/停用与加载释放。
    /// 手动启用时立即加载到内存；停用、切换、删除或退出应用时释放显存与内存。
    /// 目前不向任何 AI 调用点提供推理，避免与“节省模型消耗”功能冲突。
    /// </summary>
    internal sealed class LocalModelService : IDisposable
    {
        private static readonly Lazy<LocalModelService> LazyInstance = new(() => new LocalModelService());

        /// <summary>全局共享实例。</summary>
        public static LocalModelService Instance => LazyInstance.Value;

        private readonly LocalModelRuntime _runtime = new();
        private readonly LocalModelDeploymentService _deployment = new();

        /// <summary>加载闸门：同一时间只允许一次加载，避免并发加载两个模型。</summary>
        private readonly SemaphoreSlim _loadGate = new(1, 1);

        private Task<bool>? _loadTask;
        private string? _failedFileName;
        private bool _suspendWarmUp;
        private bool _disposed;

        /// <summary>当前是否有模型已加载（可以立即本地推理）。</summary>
        public bool IsLoaded => _runtime.IsRunning;

        /// <summary>读取当前启用的模型；未启用时返回 null。</summary>
        public DeployedModel? GetActiveModel()
        {
            return _deployment.LoadActive();
        }

        /// <summary>读取当前已加载到内存的模型；未加载时返回 null。</summary>
        public DeployedModel? GetLoadedModel()
        {
            if (!_runtime.IsRunning || string.IsNullOrEmpty(_runtime.LoadedPath))
            {
                return null;
            }

            return _deployment.LoadAll().FirstOrDefault(m =>
                string.Equals(m.FilePath, _runtime.LoadedPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 当前启用模型的运行时是否已就绪；就绪时返回可直接推理的运行时。
        /// 未启用、未加载或加载的仍是别的模型时返回 false。
        /// </summary>
        public bool TryGetReadyRuntime(out LocalModelRuntime runtime)
        {
            runtime = _runtime;
            var active = _deployment.LoadActive();
            return active != null && IsReady(active.FilePath);
        }

        /// <summary>
        /// 请求后台加载当前启用的模型：立即返回，不阻塞本次调用。
        /// 用于应用启动后第一次需要本地推理的场景（启动、重启、开机自启动都不会提前加载）。
        /// 已有加载任务在进行、已加载同名模型或上次加载失败时不会重复触发。
        /// </summary>
        public void RequestWarmUp()
        {
            if (_disposed || _suspendWarmUp)
            {
                return;
            }

            var active = _deployment.LoadActive();
            if (active == null || !File.Exists(active.FilePath))
            {
                return;
            }

            if (IsReady(active.FilePath) || _loadTask is { IsCompleted: false })
            {
                return;
            }

            if (_failedFileName != null &&
                string.Equals(_failedFileName, active.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Log.Info($"[本地模型] 请求后台加载：{active.ModelName}");
            _loadTask = Task.Run(() => LoadAsync(active));
        }

        /// <summary>
        /// 启用指定模型并立即加载：先记录启用项，旧模型在加载流程中释放。
        /// 返回是否加载成功；失败时保持启用状态，重新开关可重试。
        /// </summary>
        public async Task<bool> EnableAsync(string fileName)
        {
            var target = _deployment.LoadAll().FirstOrDefault(m =>
                string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Log.Warning($"[本地模型] 要启用的模型不在部署记录中：{fileName}");
                return false;
            }

            var active = _deployment.LoadActive();
            var isSameActive = active != null &&
                               string.Equals(active.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);

            // 先落盘并立即切换启用项，界面开关可以马上更新；旧模型的释放放到加载流程里做
            if (!isSameActive)
            {
                _deployment.SetActive(target.FileName);
            }

            _failedFileName = null;
            Log.Info($"[本地模型] 已启用：{target.ModelName}（立即加载）");
            _loadTask = LoadAsync(target);
            return await _loadTask;
        }

        /// <summary>停用当前模型：立即停止并释放运行时，再清空启用记录。</summary>
        public async Task DisableAsync()
        {
            var active = _deployment.LoadActive();
            _suspendWarmUp = true;
            try
            {
                await StopRuntimeAsync();
                _deployment.ClearActive();
            }
            finally
            {
                _suspendWarmUp = false;
            }

            _failedFileName = null;
            Log.Info(active != null ? $"[本地模型] 已停用：{active.ModelName}" : "[本地模型] 已停用");
        }

        /// <summary>删除模型：若它是当前启用或已加载的模型，先停止释放，再删除文件与记录。</summary>
        public async Task DeleteModelAsync(DeployedModel model)
        {
            var active = _deployment.LoadActive();
            var isActive = active != null &&
                           string.Equals(active.FileName, model.FileName, StringComparison.OrdinalIgnoreCase);
            var isLoaded = _runtime.IsRunning &&
                           string.Equals(_runtime.LoadedPath, model.FilePath, StringComparison.OrdinalIgnoreCase);

            _suspendWarmUp = true;
            try
            {
                if (isActive || isLoaded)
                {
                    await StopRuntimeAsync();
                }

                await Task.Run(() => _deployment.DeleteFiles(model));
            }
            finally
            {
                _suspendWarmUp = false;
            }

            _failedFileName = null;
        }

        /// <summary>加载当前启用的模型；等锁期间若已切换/停用则放弃本次加载。</summary>
        private async Task<bool> LoadAsync(DeployedModel target)
        {
            await _loadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return false;
                }

                var active = _deployment.LoadActive();
                if (active == null ||
                    !string.Equals(active.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (IsReady(target.FilePath))
                {
                    return true;
                }

                // 切换模型时旧模型可能仍占着显存，先释放再加载新模型
                if (_runtime.IsRunning)
                {
                    await _runtime.StopWhenIdleAsync().ConfigureAwait(false);
                }

                var hardware = HardwareProbe.Detect();
                var device = SettingsService.Instance.LocalModelDevice;
                var modelBytes = new FileInfo(target.FilePath).Length;
                var gpuLayers = LocalModelRuntime.ResolveGpuLayerCount(device, hardware, modelBytes);

                Log.Info($"[本地模型] 开始加载：{target.ModelName}" +
                         $"（设备 {device}，GPU 层数 {gpuLayers}）");

                await _runtime.StartAsync(target.FilePath, gpuLayers).ConfigureAwait(false);
                if (_disposed)
                {
                    _runtime.Stop();
                    return false;
                }

                _failedFileName = null;
                Log.Info("[本地模型] 加载完成");
                return true;
            }
            catch (Exception ex)
            {
                _failedFileName = target.FileName;
                Log.Error($"[本地模型] 加载失败：{target.ModelName}（{ex.Message}）");
                return false;
            }
            finally
            {
                _loadGate.Release();
            }
        }

        /// <summary>等待加载任务收尾后停止运行时；加载本身无法取消，只能等它完成再释放。</summary>
        private async Task StopRuntimeAsync()
        {
            var loadTask = _loadTask;
            if (loadTask is { IsCompleted: false })
            {
                try
                {
                    await loadTask.ConfigureAwait(false);
                }
                catch
                {
                    // 加载失败已在 LoadAsync 内记录，这里忽略异常
                }
            }

            await _runtime.StopWhenIdleAsync().ConfigureAwait(false);
        }

        /// <summary>运行时是否已加载指定路径的模型。</summary>
        private bool IsReady(string filePath)
        {
            return _runtime.IsRunning &&
                   string.Equals(_runtime.LoadedPath, filePath, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 退出时不长时间等待加载完成，避免拖慢关闭；进程结束后系统会回收资源
            try
            {
                _loadTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // 退出流程不因加载异常中断
            }

            // 加载仍在进行时不要释放原生上下文，避免与加载线程竞争；交由进程退出回收
            if (_loadTask is { IsCompleted: false })
            {
                return;
            }

            _runtime.Dispose();
            _loadGate.Dispose();
        }
    }
}
