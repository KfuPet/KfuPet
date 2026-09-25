using System.Text;
using System.Threading.Channels;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 日志落盘器：把日志异步追加写入本地文件，供用户自查与反馈问题。
    /// 调用线程只入队，真正的写入由后台任务完成，不阻塞 UI；
    /// 每次启动一个文件（KfuPet-yyyyMMdd-HHmmss.log），启动时清理旧文件只留最近几份。
    /// </summary>
    internal class LogFileWriter : IDisposable
    {
        /// <summary>日志目录：%AppData%\KfuPet\Logs（与项目其他本地数据同级）。</summary>
        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Logs");

        /// <summary>日志目录内最多保留的文件数（含本次启动产生的那一份）。</summary>
        private const int MaxFiles = 4;

        /// <summary>写入队列容量：写盘跟不上时丢最旧的日志，绝不阻塞调用线程。</summary>
        private const int QueueCapacity = 1024;

        /// <summary>退出时等待队列写完的时限。</summary>
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

        private readonly Channel<LogEntry> _queue = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pumpTask;

        /// <summary>本次运行的日志文件路径，构造时按启动时刻定好，之后不再变化。</summary>
        public string FilePath { get; }

        public LogFileWriter()
        {
            FilePath = Path.Combine(LogDirectory, $"KfuPet-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
        }

        /// <summary>
        /// 创建日志目录，并把旧日志清理到只剩 <see cref="MaxFiles"/> - 1 份，
        /// 给本次启动的文件留出位置（这样目录里始终不超过 MaxFiles 份）。
        /// 返回 false 表示目录不可用（无权限、磁盘异常等），
        /// 此时不要创建落盘器，日志保持只进内存。
        /// </summary>
        public static bool Prepare()
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogDirectory);
                CleanupOldFiles(MaxFiles - 1);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>写入一条日志：只入队，不做任何阻塞操作。</summary>
        public void Write(LogEntry entry)
        {
            _queue.Writer.TryWrite(entry);
        }

        public void Dispose()
        {
            // 先正常收尾，让已入队的日志写完，再考虑强制取消
            _queue.Writer.TryComplete();
            try
            {
                if (!_pumpTask.Wait(DrainTimeout))
                {
                    _cts.Cancel();
                }
            }
            catch
            {
                // 写盘任务异常不影响退出
            }

            _cts.Dispose();
        }

        /// <summary>保留最新的 keepCount 份日志，其余删除；单个文件删除失败不影响其他文件。</summary>
        private static void CleanupOldFiles(int keepCount)
        {
            var files = System.IO.Directory.GetFiles(LogDirectory, "KfuPet-*.log");
            // 文件名带启动时刻且定长，直接按名称排序即可判新旧
            Array.Sort(files, StringComparer.Ordinal);

            for (var i = 0; i < files.Length - keepCount; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                    // 文件被占用或删除失败：忽略，下次启动再试
                }
            }
        }

        private async Task PumpAsync(CancellationToken token)
        {
            try
            {
                await foreach (var entry in _queue.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        await File.AppendAllTextAsync(FilePath, Format(entry), Encoding.UTF8, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // 单条写入失败（磁盘满、被杀软锁定等）不终止后续写入
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
        }

        /// <summary>按「本地时间 级别 正文」格式化一条日志。</summary>
        private static string Format(LogEntry entry) =>
            $"[{entry.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
    }
}
