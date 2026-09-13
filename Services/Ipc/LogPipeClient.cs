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

        /// <summary>
        /// 连接服务端并开始接收日志。重复调用会先释放上一次连接。
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LogPipeClient));
            }

            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await stream.ConnectAsync(_connectTimeoutMs, ct);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            // 重连前释放旧连接并复位断开标记，避免句柄泄漏、以及断开事件只上报一次
            ReleaseConnection();

            _stream = stream;
            _reader = new StreamReader(stream);
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(ReadLoop);
        }

        private async Task ReadLoop()
        {
            var cts = _cts;
            var reader = _reader;
            if (cts == null || reader == null)
            {
                return;
            }

            var token = cts.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line == null)
                    {
                        // 服务端关闭了管道
                        RaiseDisconnected();
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Dispatch(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 主动断开，不视为异常
            }
            catch (Exception)
            {
                // 连接中断或读取失败：统一按断开处理，
                // 不让异常逃逸出读取任务（该任务无人 await，异常会被静默丢弃）
                RaiseDisconnected();
            }
        }

        /// <summary>
        /// 解析并派发一条日志：无法解析的行不会被转发，
        /// 订阅者抛出的异常也不会中断后续日志接收。
        /// </summary>
        private void Dispatch(string line)
        {
            LogMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<LogMessage>(line);
            }
            catch (JsonException)
            {
                return;
            }

            if (message == null)
            {
                return;
            }

            try
            {
                LogReceived?.Invoke(this, message);
            }
            catch
            {
                // 订阅者异常不影响后续日志接收
            }
        }

        private void RaiseDisconnected()
        {
            if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>释放当前连接并复位断开标记，供重连与 Dispose 复用。</summary>
        private void ReleaseConnection()
        {
            var cts = _cts;
            var readTask = _readTask;
            _cts = null;
            _readTask = null;

            cts?.Cancel();
            try
            {
                readTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 等待读取任务结束时忽略
            }

            _reader?.Dispose();
            _reader = null;
            _stream?.Dispose();
            _stream = null;
            cts?.Dispose();

            Interlocked.Exchange(ref _disconnectedRaised, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseConnection();
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
