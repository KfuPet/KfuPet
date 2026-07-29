using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using KfuPet.Services;
using KfuPet.Services.Commands;

namespace KfuPet.Services.Ipc
{
    internal class NamedPipeServer : IDisposable
    {
        private const string PipeName = "KfuPet.Skeleton";
        private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

        private readonly CommandDispatcher _dispatcher;
        private readonly Application _application;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private Task? _monitorTask;
        private bool _disposed;

        private DateTime _lastClientActivity = DateTime.MinValue;
        private bool _isClientConnected;

        public bool IsRunning => _listenTask != null && !_cts?.IsCancellationRequested == true;

        /// <summary>
        /// 工具端是否已连接（在超时窗口内收到过请求）。
        /// </summary>
        public bool IsClientConnected
        {
            get => _isClientConnected;
            private set
            {
                if (_isClientConnected != value)
                {
                    _isClientConnected = value;
                    ClientStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 客户端连接状态变化时触发（连接 / 断开）。
        /// </summary>
        public event EventHandler? ClientStateChanged;

        public NamedPipeServer(CommandDispatcher dispatcher, Application application)
        {
            _dispatcher = dispatcher;
            _application = application;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(ListenLoop, _cts.Token);
            _monitorTask = Task.Run(MonitorLoop, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            Task.WhenAll(
                _listenTask ?? Task.CompletedTask,
                _monitorTask ?? Task.CompletedTask
            ).Wait(TimeSpan.FromSeconds(3));
            _listenTask = null;
            _monitorTask = null;
            _cts?.Dispose();
            _cts = null;
            IsClientConnected = false;
        }

        private async Task ListenLoop()
        {
            var token = _cts!.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var serverStream = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await serverStream.WaitForConnectionAsync(token);
                    _ = HandleClientAsync(serverStream, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token);
                }
            }
        }

        private async Task MonitorLoop()
        {
            var token = _cts!.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var elapsed = DateTime.UtcNow - _lastClientActivity;
                    IsClientConnected = elapsed < ClientTimeout;
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken token)
        {
            try
            {
                using (stream)
                {
                    using var reader = new StreamReader(stream);
                    using var writer = new StreamWriter(stream) { AutoFlush = true };

                    var requestJson = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(requestJson)) return;

                    var request = JsonSerializer.Deserialize<CommandRequest>(requestJson);
                    if (request == null)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(CommandResponse.Fail("Invalid request")));
                        return;
                    }

                    var response = await DispatchOnUiThread(request);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));

                    _lastClientActivity = DateTime.UtcNow;
                }
            }
            catch
            {
                // 客户端断开或异常，忽略
            }
        }

        private Task<CommandResponse> DispatchOnUiThread(CommandRequest request)
        {
            var tcs = new TaskCompletionSource<CommandResponse>();

            _application.Dispatcher.Invoke(() =>
            {
                try
                {
                    var result = _dispatcher.Dispatch(request);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(CommandResponse.Fail(ex.Message));
                }
            });

            return tcs.Task;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
