using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KfuPet.Ipc.Client
{
    /// <summary>
    /// 日志命名管道客户端，连接 KfuPet 并实时接收日志。
    /// </summary>
    public class LogPipeClient : IDisposable
    {
        private const string PipeName = "KfuPet.Log";
        private const int DefaultConnectTimeoutMs = 5000;

        private readonly string _pipeName;
        private readonly int _connectTimeoutMs;
        private NamedPipeClientStream? _stream;
        private StreamReader? _reader;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _disposed;
        private int _disconnectedRaised;

        /// <summary>
        /// 收到一条日志时触发。
        /// </summary>
        public event EventHandler<LogMessage>? LogReceived;

        /// <summary>
        /// 服务端关闭管道或连接意外中断时触发（主动 Dispose 不触发）。
        /// </summary>
        public event EventHandler? Disconnected;

        public LogPipeClient(string pipeName = PipeName, int connectTimeoutMs = DefaultConnectTimeoutMs)
        {
            _pipeName = pipeName;
            _connectTimeoutMs = connectTimeoutMs;
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync(_connectTimeoutMs, ct);

            _stream = stream;
            _reader = new StreamReader(stream);
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(ReadLoop);
        }

        private async Task ReadLoop()
        {
            var token = _cts!.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _reader!.ReadLineAsync(token);
                    if (line == null)
                    {
                        RaiseDisconnected();
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var message = JsonSerializer.Deserialize<LogMessage>(line);
                        if (message != null)
                        {
                            LogReceived?.Invoke(this, message);
                        }
                    }
                    catch (JsonException)
                    {
                        // 忽略无法解析的行
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                RaiseDisconnected();
            }
        }

        private void RaiseDisconnected()
        {
            if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            try
            {
                _readTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 等待读取任务结束时忽略
            }

            _reader?.Dispose();
            _stream?.Dispose();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// 日志消息模型，与 KfuPet 端推送的 JSON 结构保持一致。
    /// </summary>
    public class LogMessage
    {
        public DateTime Timestamp { get; set; }

        public LogLevel Level { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
