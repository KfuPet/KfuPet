namespace KfuPet.Models
{
    /// <summary>可供下载的本地模型条目，以及它在当前机器上的可用状态。</summary>
    public sealed class LocalModelOption
    {
        /// <summary>模型名称（展示用）。</summary>
        public required string Name { get; init; }

        /// <summary>参数量，例如 1.7B / 4B / 8B。</summary>
        public required string Parameters { get; init; }

        /// <summary>量化方式，例如 Q4_K_M。</summary>
        public required string Quantization { get; init; }

        /// <summary>下载体积（字节），用于展示与下载前提示。</summary>
        public required long DownloadSizeBytes { get; init; }

        /// <summary>GGUF 文件名，下载后落盘用。</summary>
        public required string FileName { get; init; }

        /// <summary>按优先级排列的下载地址，失败时依次尝试。</summary>
        public required IReadOnlyList<string> DownloadUrls { get; init; }

        /// <summary>最低独显显存要求，0 表示不依赖独显。</summary>
        public required long MinVramBytes { get; init; }

        /// <summary>最低内存要求。</summary>
        public required long MinRamBytes { get; init; }

        /// <summary>推荐显存，达到后标记为可流畅运行。</summary>
        public required long RecommendedVramBytes { get; init; }

        /// <summary>推荐内存。</summary>
        public required long RecommendedRamBytes { get; init; }

        /// <summary>显存不足时是否允许退回 CPU 推理。</summary>
        public required bool CpuFallbackAllowed { get; init; }

        /// <summary>卡片上展示的配置要求文案。</summary>
        public required string RequirementText { get; init; }

        /// <summary>卡片上展示的用途说明。</summary>
        public required string Note { get; init; }

        /// <summary>下载体积的展示文本（GB，保留一位小数）。</summary>
        public string SizeText => $"{DownloadSizeBytes / 1024d / 1024d / 1024d:0.0} GB";

        /// <summary>当前机器上的运行状态，由 LocalModelCatalog 计算。</summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>当前机器是否满足最低运行要求。</summary>
        public bool CanRun { get; set; }

        /// <summary>是否为当前硬件下的推荐档。</summary>
        public bool IsRecommended { get; set; }

        /// <summary>该模型已经下载到本机，列表中置灰且不可重复选择。</summary>
        public bool IsDownloaded { get; set; }

        /// <summary>是否显示“已下载”徽章。</summary>
        public bool ShowDownloadedBadge => IsDownloaded;

        /// <summary>产品默认档（4B），与硬件无关。</summary>
        public bool IsDefault { get; init; }

        /// <summary>是否支持实时核心信息提取与归档批量提炼；1.7B 在调用点清单里标注为不可用。</summary>
        public bool SupportsMemoryExtraction { get; init; } = true;

        /// <summary>是否支持多条信息总结；1.7B 在调用点清单里标注为不可用。</summary>
        public bool SupportsMemorySummarization { get; init; } = true;

        /// <summary>是否显示“默认”徽章。</summary>
        public bool ShowDefaultBadge => IsDefault;

        /// <summary>同时是默认档时只显示“默认”，避免两个标签并排。</summary>
        public bool ShowRecommendedBadge => IsRecommended && !IsDefault;
    }
}
