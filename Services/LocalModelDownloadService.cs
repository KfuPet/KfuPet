using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>把 GGUF 模型下载到本地，支持断点续传、多地址回退与 SHA256 完整性校验。</summary>
    public static class LocalModelDownloadService
    {
        /// <summary>读写缓冲区大小（80 KB）。</summary>
        private const int BufferSize = 81920;

        /// <summary>可能携带内容 SHA256 的响应头，按优先级排列。</summary>
        private static readonly string[] IntegrityHeaders = { "X-Linked-Etag", "ETag" };

        private static readonly HttpClient Client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>临时文件路径（目标文件同目录下的 .part 文件），下载中断后保留用于续传。</summary>
        public static string GetPartPath(string targetPath) => targetPath + ".part";

        /// <summary>续传时还需要下载的字节数；没有断点时为完整下载体积。</summary>
        public static long GetRemainingBytes(LocalModelOption option, string targetPath)
        {
            return Math.Max(0, option.DownloadSizeBytes - GetFileLength(GetPartPath(targetPath)));
        }

        /// <summary>目标文件所在磁盘的可用空间（字节）；无法判断时返回 -1，调用方应跳过检查。</summary>
        public static long GetAvailableBytes(string targetPath)
        {
            try
            {
                // 目录可能还没创建，沿路径向上找到最近一个已存在的目录再取所在磁盘
                var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    directory = Path.GetDirectoryName(directory);
                }

                var root = string.IsNullOrEmpty(directory) ? null : Path.GetPathRoot(directory);
                return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                Log.Warning($"[本地模型] 无法读取磁盘剩余空间：{ex.Message}");
                return -1;
            }
        }

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

            var partPath = GetPartPath(targetPath);
            Exception? lastError = null;

            foreach (var url in option.DownloadUrls)
            {
                try
                {
                    Log.Debug($"[本地模型] 尝试下载地址：{url}");
                    var result = await DownloadFromUrlAsync(url, partPath, progress, cancellationToken);

                    if (result.ExpectedSha256 != null &&
                        !string.Equals(result.Sha256, result.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        // 摘要不一致说明文件已损坏（常见于旧断点损坏）：丢掉断点后重下，避免带病续传
                        TryDelete(partPath);
                        throw new InvalidOperationException(
                            $"文件校验失败：本地 SHA256 {result.Sha256} 与服务器声明的 {result.ExpectedSha256} 不一致");
                    }

                    File.Move(partPath, targetPath, overwrite: true);

                    if (result.ExpectedSha256 == null)
                    {
                        Log.Info($"[本地模型] 下载完成，服务器未提供 SHA256，仅记录本地摘要：{result.Sha256}");
                    }
                    else
                    {
                        Log.Info($"[本地模型] 下载完成，SHA256 校验通过：{result.Sha256}");
                    }

                    return result.Sha256;
                }
                catch (OperationCanceledException)
                {
                    // 取消时保留 .part，下次下载从断点继续
                    Log.Info($"[本地模型] 下载已取消，断点已保留：{partPath}");
                    throw;
                }
                catch (Exception ex)
                {
                    // 其它失败同样保留断点，换地址或重试时可继续
                    Log.Warning($"[本地模型] 下载地址失败：{url}（{ex.Message}）");
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                lastError == null ? "没有可用的下载地址" : $"下载失败：{lastError.Message}", lastError);
        }

        /// <summary>单地址下载（存在断点时用 Range 续传），返回本地摘要与服务器声明的摘要。</summary>
        private static async Task<DownloadResult> DownloadFromUrlAsync(
            string url,
            string partPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var resumeBytes = GetFileLength(partPath);

            while (true)
            {
                using var request = CreateRequest(url, resumeBytes);
                using var response = await Client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (resumeBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // 断点长度和服务端文件对不上（例如临时文件比文件本身还大），丢弃后从头下载
                    Log.Warning("[本地模型] 服务器不接受续传范围，改为从头下载");
                    TryDelete(partPath);
                    resumeBytes = 0;
                    continue;
                }

                response.EnsureSuccessStatusCode();

                if (resumeBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    // 该镜像不支持 Range，只能从头下载
                    Log.Info("[本地模型] 下载地址不支持续传，改为从头下载");
                    resumeBytes = 0;
                }

                var totalBytes = ResolveTotalBytes(response, resumeBytes);
                var expectedSha256 = TryGetExpectedSha256(response);

                return await ReadStreamAsync(
                    response, partPath, resumeBytes, totalBytes, expectedSha256, progress, cancellationToken);
            }
        }

        /// <summary>构造下载请求；resumeBytes 大于 0 时带 Range 头要求从断点继续。</summary>
        private static HttpRequestMessage CreateRequest(string url, long resumeBytes)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KfuPet/1.0");
            if (resumeBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(resumeBytes, null);
            }

            return request;
        }

        /// <summary>计算完整文件体积：续传响应用 Content-Range 的总长，否则用 Content-Length（续传时加上断点偏移）。</summary>
        private static long ResolveTotalBytes(HttpResponseMessage response, long resumeBytes)
        {
            if (resumeBytes > 0 && response.Content.Headers.ContentRange?.Length is { } rangeLength)
            {
                return rangeLength;
            }

            var contentLength = response.Content.Headers.ContentLength ?? -1;
            return resumeBytes > 0 && contentLength > 0 ? resumeBytes + contentLength : contentLength;
        }

        /// <summary>从响应头取服务器声明的 SHA256；Hugging Face 的 LFS 文件在 X-Linked-Etag / ETag 中给出。</summary>
        private static string? TryGetExpectedSha256(HttpResponseMessage response)
        {
            foreach (var name in IntegrityHeaders)
            {
                IEnumerable<string>? values = null;
                if (response.Headers.TryGetValues(name, out var headerValues))
                {
                    values = headerValues;
                }
                else if (response.Content.Headers.TryGetValues(name, out var contentValues))
                {
                    values = contentValues;
                }

                if (values == null)
                {
                    continue;
                }

                foreach (var raw in values)
                {
                    if (TryParseSha256(raw) is { } hash)
                    {
                        return hash;
                    }
                }
            }

            return null;
        }

        /// <summary>解析 64 位十六进制的摘要文本；ETag 可能带引号、W/ 前缀或 sha256: 前缀。</summary>
        private static string? TryParseSha256(string raw)
        {
            var token = raw.Trim();
            if (token.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
            }

            token = token.Trim('"');
            if (token.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                token = token["sha256:".Length..];
            }

            return token.Length == 64 && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
        }

        /// <summary>读取响应流写入 .part，同时增量计算 SHA256。</summary>
        private static async Task<DownloadResult> ReadStreamAsync(
            HttpResponseMessage response,
            string partPath,
            long resumeBytes,
            long totalBytes,
            string? expectedSha256,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            if (resumeBytes > 0)
            {
                // 续传前先把磁盘上的旧片段读一遍计入摘要；本地读盘远比重下几个 GB 便宜
                await AppendHashAsync(partPath, hasher, cancellationToken);
            }

            await using var target = new FileStream(
                partPath,
                resumeBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            var buffer = new byte[BufferSize];
            long receivedBytes = resumeBytes;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            long lastBytes = resumeBytes;
            double speed = 0;

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

            return new DownloadResult(
                Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant(), expectedSha256);
        }

        /// <summary>把已有文件内容追加进增量哈希；用于续传时补齐前半段的摘要。</summary>
        private static async Task AppendHashAsync(
            string path,
            IncrementalHash hasher,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];

            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                hasher.AppendData(buffer, 0, read);
            }
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch
            {
                return 0;
            }
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

        /// <summary>单次下载结果：本地计算的摘要与服务器声明的摘要（服务器没给时为 null）。</summary>
        private sealed record DownloadResult(string Sha256, string? ExpectedSha256);
    }
}
