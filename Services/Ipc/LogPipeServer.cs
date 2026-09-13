using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using KfuPet.Models;

namespace KfuPet.Services.Ipc
{
    /// <summary>
    /// 日志命名管道服务端，向外部开发者工具单向推送日志。
    /// 同一时刻只服务一个客户端；日志写入只入队、不直接写管道，
    /// 真正的写入由后台发送任务完成，避免阻塞调用线程（可能是 UI 线程）。
    /// </summary>
    internal class LogPipeServer : IDisposable
    {
        private const string PipeName = "KfuPet.Log";

        private readonly LogService _logService;
        private readonly object _clientLock = new();
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private ClientConnection? _client;
        private bool _disposed;

        public LogPipeServer(LogService logService)
        {
            _logService = logService;
        }

        public void Start()
        {
            if (_disposed || _listenTask != null) return;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _listenTask = Task.Run(() => ListenLoop(cts.Token));
        }

        public void Stop()
        {
            var cts = _cts;
            var listenTask = _listenTask;
            _cts = null;
            _listenTask = null;

            cts?.Cancel();

            try
            {
                listenTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 等待超时或任务异常时忽略
            }

            DisposeClient();

            if (cts == null) return;

            // 任务没在超时内结束时不能立即释放令牌源，否则仍在运行的任务会用到已释放的令牌；
            // 改为等任务结束后再释放。
            if (listenTask == null || listenTask.IsCompleted)
            {
                cts.Dispose();
            }
            else
            {
                _ = listenTask.ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // 单实例：同一时刻只服务一个客户端，第二个客户端需要等前一个断开后才能连上
                var stream = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await stream.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    stream.Dispose();
                    break;
                }
                catch
                {
                    stream.Dispose();
                    try
                    {
                        await Task.Delay(500, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    await ServeClientAsync(stream, token);
                }
                catch
                {
                    // 单个客户端会话出错不应终止监听，继续等待下一个客户端
                }
            }
        }

        /// <summary>
        /// 服务一个客户端，直到它断开或服务端停止。
        /// </summary>
        private async Task ServeClientAsync(NamedPipeServerStream stream, CancellationToken token)
        {
            using (stream)
            {
                var connection = new ClientConnection(stream, _logService);
                lock (_clientLock)
                {
                    _client = connection;
                }

                try
                {
                    // 客户端不会发送数据，这里通过读取来检测客户端断开：
                    // 客户端关闭后 ReadAsync 返回 0，即可回收连接并继续等待下一个客户端。
                    var buffer = new byte[1];
                    while (!token.IsCancellationRequested)
                    {
                        if (await stream.ReadAsync(buffer, token) == 0)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
                finally
                {
                    lock (_clientLock)
                    {
                        if (ReferenceEquals(_client, connection))
                        {
                            _client = null;
                        }
                    }

                    connection.Dispose();
                }
            }
        }

        /// <summary>停止并回收当前客户端连接。</summary>
        private void DisposeClient()
        {
            ClientConnection? client;
            lock (_clientLock)
            {
                client = _client;
                _client = null;
            }

            client?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        /// <summary>
        /// 单个日志客户端连接：持有待发送队列与后台发送任务。
        /// 入队不会阻塞，避免日志调用方（可能是 UI 线程）被管道写入拖住。
        /// </summary>
        private sealed class ClientConnection : IDisposable
        {
            /// <summary>待发送队列容量，超出后丢弃最旧的日志，避免内存无上限增长。</summary>
            private const int QueueCapacity = 1000;

            /// <summary>单条日志内容的最大长度，超出部分截断后再推送。</summary>
            private const int MaxMessageLength = 4096;

            /// <summary>日志被截断时追加的提示。</summary>
            private const string TruncatedSuffix = "…（日志过长已截断）";

            private readonly StreamWriter _writer;
            private readonly Channel<LogEntry> _queue;
            private readonly IDisposable _subscription;

            public ClientConnection(Stream stream, LogService logService)
            {
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _queue = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(QueueCapacity)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

                _subscription = logService.Subscribe(OnEntryAdded, EnqueueBacklog);
                _ = Task.Run(PumpAsync);
            }

            /// <summary>历史日志在订阅锁内先入队，保证排在实时日志之前。</summary>
            private void EnqueueBacklog(IReadOnlyList<LogEntry> entries)
            {
                foreach (var entry in entries)
                {
                    _queue.Writer.TryWrite(entry);
                }
            }

            /// <summary>实时日志只入队，不做任何阻塞操作。</summary>
            private void OnEntryAdded(object? sender, LogEntry entry)
            {
                _queue.Writer.TryWrite(entry);
            }

            private async Task PumpAsync()
            {
                try
                {
                    await foreach (var entry in _queue.Reader.ReadAllAsync())
                    {
                        await _writer.WriteLineAsync(Serialize(entry));
                    }
                }
                catch
                {
                    // 客户端断开或写入失败：结束发送，由外层检测到断开后回收连接
                    _queue.Writer.TryComplete();
                }
                finally
                {
                    try
                    {
                        _writer.Dispose();
                    }
                    catch
                    {
                        // 管道已断开，忽略释放异常
                    }
                }
            }

            /// <summary>把一条日志序列化为推送用的 JSON，超长内容先截断。</summary>
            private static string Serialize(LogEntry entry)
            {
                if (entry.Message.Length <= MaxMessageLength)
                {
                    return JsonSerializer.Serialize(entry);
                }

                return JsonSerializer.Serialize(new LogEntry
                {
                    Timestamp = entry.Timestamp,
                    Level = entry.Level,
                    Message = entry.Message.Substring(0, MaxMessageLength) + TruncatedSuffix
                });
            }

            public void Dispose()
            {
                _subscription.Dispose();
                _queue.Writer.TryComplete();
            }
        }
    }
}
