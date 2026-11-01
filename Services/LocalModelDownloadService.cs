using System.Diagnostics;
using System.Security.Cryptography;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>下载进度快照。</summary>
    public sealed record DownloadProgress(long ReceivedBytes, long TotalBytes, double BytesPerSecond)
    {
        /// <summary>已下载百分比（0–100）。</summary>
        public double Percent => TotalBytes > 0
            ? Math.Clamp(ReceivedBytes * 100d / TotalBytes, 0, 100)
            : 0;

        /// <summary>已下载体积的展示文本。</summary>
        public string ReceivedText => FormatBytes(ReceivedBytes);

        /// <summary>总体积的展示文本；服务端未告知总长时显示“未知”。</summary>
        public string TotalText => TotalBytes > 0 ? FormatBytes(TotalBytes) : "未知";

        /// <summary>下载速度的展示文本。</summary>
        public string SpeedText => BytesPerSecond > 0
            ? $"{BytesPerSecond / 1024d / 1024d:0.0} MB/s"
            : "—";

        /// <summary>预计剩余秒数；速度或总量未知时为空。</summary>
        public double? RemainingSeconds => BytesPerSecond > 0 && TotalBytes > 0
            ? Math.Max(0, (TotalBytes - ReceivedBytes) / BytesPerSecond)
            : null;

        /// <summary>剩余时间的展示文本。</summary>
        public string EtaText => RemainingSeconds is { } seconds
            ? $"剩余 {FormatDuration(seconds)}"
            : "剩余时间计算中…";

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
            }

            if (bytes >= 1024L * 1024)
            {
                return $"{bytes / 1024d / 1024d:0.0} MB";
            }

            return $"{bytes / 1024d:0.0} KB";
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds >= 3600)
            {
                return $"{(int)(seconds / 3600)} 小时 {(int)(seconds % 3600 / 60)} 分";
            }

            if (seconds >= 60)
            {
                return $"{(int)(seconds / 60)} 分 {(int)(seconds % 60)} 秒";
            }

            return $"{(int)seconds} 秒";
        }
    }

    /// <summary>把 GGUF 模型下载到本地，支持多地址回退与取消。</summary>
    public static class LocalModelDownloadService
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>下载模型到目标路径，返回 GGUF 文件的 SHA256（边下载边计算，不额外读盘）。</summary>
        public static async Task<string> DownloadAsync(
            LocalModelOption option,
            string targetPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var partPath = targetPath + ".part";
            Exception? lastError = null;

            foreach (var url in option.DownloadUrls)
            {
                try
                {
                    var sha256 = await DownloadFromUrlAsync(url, partPath, progress, cancellationToken);
                    File.Move(partPath, targetPath, overwrite: true);
                    return sha256;
                }
                catch (OperationCanceledException)
                {
                    TryDelete(partPath);
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    TryDelete(partPath);
                }
            }

            throw new InvalidOperationException(
                lastError == null ? "没有可用的下载地址" : $"下载失败：{lastError.Message}", lastError);
        }

        /// <summary>单地址下载，同时增量计算 SHA256；返回十六进制小写摘要。</summary>
        private static async Task<string> DownloadFromUrlAsync(
            string url,
            string partPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KfuPet/1.0");

            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long receivedBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            long lastBytes = 0;
            double speed = 0;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hasher.AppendData(buffer, 0, read);
                receivedBytes += read;

                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastReport >= TimeSpan.FromMilliseconds(250))
                {
                    var instantSpeeds = (receivedBytes - lastBytes) / (elapsed - lastReport).TotalSeconds;
                    speed = speed <= 0 ? instantSpeeds : speed * 0.7 + instantSpeeds * 0.3;
                    lastReport = elapsed;
                    lastBytes = receivedBytes;
                    progress?.Report(new DownloadProgress(receivedBytes, totalBytes, speed));
                }
            }

            progress?.Report(new DownloadProgress(receivedBytes, totalBytes, speed));

            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 临时文件删不掉不影响后续重试
            }
        }
    }
}
