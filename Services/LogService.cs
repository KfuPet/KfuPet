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
        private int _count;

        /// <summary>
        /// 新增一条日志时触发，供日志管道实时转发。
        /// </summary>
        public event EventHandler<LogEntry>? EntryAdded;

        /// <summary>
        /// 获取当前缓冲的全部日志（按时间顺序）。
        /// </summary>
        public IReadOnlyList<LogEntry> GetEntries()
        {
            return _entries.ToArray();
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

            _entries.Enqueue(entry);
            if (Interlocked.Increment(ref _count) > MaxEntries)
            {
                _entries.TryDequeue(out _);
                Interlocked.Decrement(ref _count);
            }

            EntryAdded?.Invoke(this, entry);
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);

        public void Info(string message) => Log(LogLevel.Info, message);

        public void Warning(string message) => Log(LogLevel.Warning, message);

        public void Error(string message) => Log(LogLevel.Error, message);
    }
}
