using System.IO.Pipes;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services.Ipc
{
    /// <summary>
    /// 日志命名管道服务端，向外部开发者工具单向推送日志。
    /// </summary>
    internal class LogPipeServer : IDisposable
    {
        private const string PipeName = "KfuPet.Log";

        private readonly LogService _logService;
        private readonly object _writeLock = new();
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private StreamWriter? _writer;
        private bool _disposed;

        public LogPipeServer(LogService logService)
        {
            _logService = logService;
        }

        public void Start()
        {
            if (_listenTask != null) return;

            _cts = new CancellationTokenSource();
            _logService.EntryAdded += OnEntryAdded;
            _listenTask = Task.Run(ListenLoop, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _logService.EntryAdded -= OnEntryAdded;

            try
            {
                _listenTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 等待超时或任务异常时忽略
            }

            lock (_writeLock)
            {
                _writer?.Dispose();
                _writer = null;
            }

            _listenTask = null;
            _cts?.Dispose();
            _cts = null;
        }

        private async Task ListenLoop()
        {
            var token = _cts!.Token;

            while (!token.IsCancellationRequested)
            {
                var stream = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
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

                await ServeClientAsync(stream, token);
            }
        }

        private async Task ServeClientAsync(NamedPipeServerStream stream, CancellationToken token)
        {
            using (stream)
            using (var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                lock (_writeLock)
                {
                    _writer = writer;
                }

                try
                {
                    // 连接后先补发历史日志
                    WriteBacklog(writer, _logService.GetEntries());

                    // 客户端不会发送数据，这里通过读取来检测客户端断开：
                    // 客户端关闭后 ReadAsync 返回 0，即可回收连接并继续等待下一个客户端。
                    var buffer = new byte[1];
                    while (!token.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(buffer, token);
                        if (read == 0)
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
                    lock (_writeLock)
                    {
                        if (ReferenceEquals(_writer, writer))
                        {
                            _writer = null;
                        }
                    }
                }
            }
        }

        private void OnEntryAdded(object? sender, LogEntry entry)
        {
            lock (_writeLock)
            {
                if (_writer == null) return;
                WriteLineSafe(_writer, entry);
            }
        }

        private void WriteBacklog(StreamWriter writer, IReadOnlyList<LogEntry> entries)
        {
            lock (_writeLock)
            {
                foreach (var entry in entries)
                {
                    WriteLineSafe(writer, entry);
                }
            }
        }

        private static void WriteLineSafe(StreamWriter writer, LogEntry entry)
        {
            try
            {
                writer.WriteLine(JsonSerializer.Serialize(entry));
            }
            catch
            {
                // 客户端断开或写入失败时忽略
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
