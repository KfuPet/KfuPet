using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KfuPet.Models;
using LLama;
using LLama.Common;

namespace KfuPet.Services
{
    /// <summary>
    /// 进程内的 LLamaSharp 运行时：加载本地 GGUF 权重并创建上下文，
    /// 同一时间只允许加载一个模型、只跑一路推理；停止或卸载时释放显存与内存。
    /// </summary>
    internal sealed class LocalModelRuntime : IDisposable
    {
        /// <summary>推理上下文长度：与参考项目保持一致，满足短句与 JSON 提取类调用。</summary>
        private const int ContextSize = 8192;

        /// <summary>GPU 加速时交给模型的层数；999 表示全部层都放到显卡。</summary>
        private const int GpuLayerCountAll = 999;

        /// <summary>自动判断显存是否够用时，要求模型体积 ×1.2 的余量。</summary>
        private const double VramHeadroomFactor = 1.2;

        /// <summary>推理闸门：同一时间只允许一路推理，避免并发争抢同一份上下文。</summary>
        private readonly SemaphoreSlim _inferenceGate = new(1, 1);

        private LLamaWeights? _weights;
        private LLamaContext? _context;
        private ModelParams? _parameters;
        private bool _disposed;

        /// <summary>模型是否已加载（true 表示可以推理）。</summary>
        public bool IsRunning => _weights != null;

        /// <summary>当前是否已有一路推理正在进行（用于短句调用快速回退远端，避免排队等待）。</summary>
        public bool IsBusy => _inferenceGate.CurrentCount == 0;

        /// <summary>当前已加载的模型文件路径；未加载时为空。</summary>
        public string? LoadedPath { get; private set; }

        /// <summary>
        /// 加载模型；加载失败会抛出异常，由调用方提示用户。
        /// 已加载其他模型时需先停止，避免两个模型同时占用显存。
        /// </summary>
        /// <param name="modelPath">GGUF 文件路径。</param>
        /// <param name="gpuLayerCount">放到 GPU 的层数；0 表示纯 CPU 推理。</param>
        public Task StartAsync(string modelPath, int gpuLayerCount)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalModelRuntime));
            }

            if (IsRunning)
            {
                if (string.Equals(LoadedPath, modelPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException("已有其他本地模型在运行，请先停止后再加载新模型。");
            }

            return Task.Run(() =>
            {
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = ContextSize,
                    GpuLayerCount = gpuLayerCount
                };

                LLamaWeights? weights = null;
                try
                {
                    weights = LLamaWeights.LoadFromFile(parameters);
                    var context = weights.CreateContext(parameters);
                    _weights = weights;
                    _context = context;
                    _parameters = parameters;
                    LoadedPath = modelPath;
                    Log.Info($"[本地模型] 运行时已加载：{modelPath}（GPU 层数 {gpuLayerCount}）");
                }
                catch (Exception ex)
                {
                    weights?.Dispose();
                    Log.Error($"[本地模型] 运行时加载失败：{modelPath}（{ex.Message}）");
                    throw;
                }
            });
        }

        /// <summary>用当前模型跑一次短推理：套用 GGUF 自带的对话模板，适合台词、判断类调用。</summary>
        /// <param name="systemPrompt">系统提示词（角色人设与任务说明）。</param>
        /// <param name="userMessage">本轮用户消息或待处理内容。</param>
        /// <param name="maxTokens">最多生成的 token 数。</param>
        public async Task<string> InferAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens = 96,
            CancellationToken cancellationToken = default)
        {
            var weights = _weights ?? throw new InvalidOperationException("本地模型尚未加载");
            var parameters = _parameters ?? throw new InvalidOperationException("本地模型参数缺失");

            await _inferenceGate.WaitAsync(cancellationToken);
            try
            {
                var executor = new StatelessExecutor(weights, parameters)
                {
                    ApplyTemplate = true,
                    SystemMessage = systemPrompt
                };

                var inferenceParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new[] { "<|im_end|>", "<|endoftext|>" }
                };

                var builder = new StringBuilder();
                await foreach (var token in executor.InferAsync(
                                   userMessage, inferenceParams, cancellationToken))
                {
                    builder.Append(token);
                }

                return builder.ToString().Trim();
            }
            finally
            {
                _inferenceGate.Release();
            }
        }

        /// <summary>等待当前推理结束后再释放，避免在推理过程中销毁原生上下文。</summary>
        public async Task StopWhenIdleAsync()
        {
            await _inferenceGate.WaitAsync();
            try
            {
                Stop();
            }
            finally
            {
                _inferenceGate.Release();
            }
        }

        /// <summary>释放上下文与权重，下次启动时重新加载。</summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            var path = LoadedPath;
            _context?.Dispose();
            _context = null;

            _weights?.Dispose();
            _weights = null;
            _parameters = null;
            LoadedPath = null;

            Log.Info($"[本地模型] 运行时已释放：{path}");
        }

        /// <summary>按推理设备选择换算 GPU 层数：自动档在显存 ≥ 模型体积 ×1.2 时全量上 GPU。</summary>
        public static int ResolveGpuLayerCount(
            InferenceDevice device, HardwareInfo hardware, long modelSizeBytes)
        {
            var useGpu = device switch
            {
                InferenceDevice.Gpu => true,
                InferenceDevice.Cpu => false,
                _ => hardware.HasDedicatedGpu &&
                     hardware.DedicatedVideoMemoryBytes >= modelSizeBytes * VramHeadroomFactor
            };

            if (device == InferenceDevice.Gpu && !hardware.HasDedicatedGpu)
            {
                Log.Warning("[本地模型] 未检测到独显，已按 CPU 运行");
                useGpu = false;
            }

            return useGpu ? GpuLayerCountAll : 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _inferenceGate.Dispose();
        }
    }
}
