using System.Collections.Concurrent;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 全局日志服务，负责收集日志并提供给日志管道推送。
    /// </summary>
    internal class LogService
    {
        private const int MaxEntries = 1000;

        private readonly ConcurrentQueue<LogEntry> _entries = new();
        private readonly object _gate = new();

        /// <summary>实时日志订阅者，仅在 <see cref="_gate"/> 锁内读写。</summary>
        private EventHandler<LogEntry>? _entryAdded;

        /// <summary>
        /// 订阅日志：在同一把锁内先回调历史日志、再挂上实时回调，
        /// 保证历史与实时之间不重复、不遗漏、不乱序。
        /// 回调内只做入队等轻量操作，不要做耗时或阻塞操作。
        /// </summary>
        /// <param name="handler">实时日志回调。</param>
        /// <param name="backlogCallback">历史日志回调，收到最近 <see cref="MaxEntries"/> 条（按时间顺序）。</param>
        /// <returns>订阅句柄，释放后不再接收实时日志。</returns>
        public IDisposable Subscribe(EventHandler<LogEntry> handler, Action<IReadOnlyList<LogEntry>> backlogCallback)
        {
            lock (_gate)
            {
                backlogCallback(_entries.ToArray());
                _entryAdded += handler;
            }

            return new Subscription(this, handler);
        }

        /// <summary>
        /// 写入一条日志。
        /// </summary>
        public void Log(LogLevel level, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message
            };

            lock (_gate)
            {
                _entries.Enqueue(entry);
                if (_entries.Count > MaxEntries)
                {
                    _entries.TryDequeue(out _);
                }

                try
                {
                    _entryAdded?.Invoke(this, entry);
                }
                catch
                {
                    // 订阅者异常不阻断日志写入，也不影响其他订阅者
                }
            }
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);

        public void Info(string message) => Log(LogLevel.Info, message);

        public void Warning(string message) => Log(LogLevel.Warning, message);

        public void Error(string message) => Log(LogLevel.Error, message);

        /// <summary>取消订阅。</summary>
        private void Unsubscribe(EventHandler<LogEntry> handler)
        {
            lock (_gate)
            {
                _entryAdded -= handler;
            }
        }

        /// <summary>日志订阅句柄，释放后不再接收实时日志。</summary>
        private sealed class Subscription : IDisposable
        {
            private readonly LogService _service;
            private EventHandler<LogEntry>? _handler;

            public Subscription(LogService service, EventHandler<LogEntry> handler)
            {
                _service = service;
                _handler = handler;
            }

            public void Dispose()
            {
                var handler = Interlocked.Exchange(ref _handler, null);
                if (handler != null)
                {
                    _service.Unsubscribe(handler);
                }
            }
        }
    }
}
