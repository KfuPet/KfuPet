namespace KfuPet.Models
{
    /// <summary>已下载到本机的本地模型记录，持久化在 Config\deployed-models.json。</summary>
    public sealed class DeployedModel
    {
        /// <summary>模型名称。</summary>
        public required string ModelName { get; init; }

        /// <summary>GGUF 文件名。</summary>
        public required string FileName { get; init; }

        /// <summary>本地完整路径。</summary>
        public required string FilePath { get; init; }

        /// <summary>文件体积（字节）。</summary>
        public required long SizeBytes { get; init; }

        /// <summary>下载完成时间。</summary>
        public required DateTime DeployedAt { get; init; }

        /// <summary>GGUF 文件的 SHA256（下载时增量计算）；早期记录为空。</summary>
        public string? Sha256 { get; init; }

        /// <summary>参数量，例如 4B。</summary>
        public string? Parameters { get; init; }

        /// <summary>量化方式，例如 Q4_K_M。</summary>
        public string? Quantization { get; init; }

        /// <summary>文件体积的展示文本（GB，保留两位小数）。</summary>
        public string SizeText => $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GB";
    }
}
