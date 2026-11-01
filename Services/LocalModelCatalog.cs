using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>本地模型目录与推荐规则：三档模型按本机显存 / 内存标注状态，并选出一个推荐项。</summary>
    internal static class LocalModelCatalog
    {
        private const double GbUnit = 1024d * 1024d * 1024d;

        /// <summary>容量判定容差：8GB 显卡的实际可用显存通常略低于标称值。</summary>
        private const double Tolerance = 0.95;

        /// <summary>可流畅运行的状态文案。</summary>
        public const string StatusSmooth = "可流畅运行";

        /// <summary>配置偏低但可运行的状态文案。</summary>
        public const string StatusLow = "可运行（配置偏低）";

        /// <summary>配置不足的状态文案。</summary>
        public const string StatusInsufficient = "配置不足";

        /// <summary>三档内置本地模型目录（下载地址优先国内镜像，失败时回退官方地址）。</summary>
        public static IReadOnlyList<LocalModelOption> All { get; } = new List<LocalModelOption>
        {
            new()
            {
                Name = "Qwen3-1.7B-Instruct",
                Parameters = "1.7B",
                Quantization = "Q4_K_M",
                DownloadSizeBytes = (long)(1.11 * GbUnit),
                FileName = "Qwen3-1.7B-Q4_K_M.gguf",
                DownloadUrls = new[]
                {
                    "https://hf-mirror.com/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf?download=true",
                    "https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf?download=true"
                },
                MinVramBytes = (long)(2 * GbUnit),
                MinRamBytes = (long)(4 * GbUnit),
                RecommendedVramBytes = (long)(4 * GbUnit),
                RecommendedRamBytes = (long)(8 * GbUnit),
                CpuFallbackAllowed = true,
                RequirementText = "无独显也可运行 · 最低内存 4 GB",
                Note = "最轻量档，台词与情绪判断够用",
                SupportsMemoryExtraction = false,
                SupportsMemorySummarization = false
            },
            new()
            {
                Name = "Qwen3-4B-Instruct-2507",
                Parameters = "4B",
                Quantization = "Q4_K_M",
                DownloadSizeBytes = (long)(2.5 * GbUnit),
                FileName = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
                DownloadUrls = new[]
                {
                    "https://hf-mirror.com/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf?download=true",
                    "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf?download=true"
                },
                MinVramBytes = (long)(4 * GbUnit),
                MinRamBytes = (long)(8 * GbUnit),
                RecommendedVramBytes = (long)(6 * GbUnit),
                RecommendedRamBytes = (long)(16 * GbUnit),
                CpuFallbackAllowed = true,
                RequirementText = "最低显存 4 GB · 内存 8 GB",
                Note = "默认档，短句与 JSON 提取都稳",
                IsDefault = true
            },
            new()
            {
                Name = "Qwen3-8B",
                Parameters = "8B",
                Quantization = "Q4_K_M",
                DownloadSizeBytes = (long)(5.03 * GbUnit),
                FileName = "Qwen3-8B-Q4_K_M.gguf",
                DownloadUrls = new[]
                {
                    "https://hf-mirror.com/unsloth/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf?download=true",
                    "https://huggingface.co/unsloth/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf?download=true"
                },
                MinVramBytes = (long)(6 * GbUnit),
                MinRamBytes = (long)(8 * GbUnit),
                RecommendedVramBytes = (long)(8 * GbUnit),
                RecommendedRamBytes = (long)(16 * GbUnit),
                CpuFallbackAllowed = false,
                RequirementText = "最低显存 6 GB · 内存 8 GB · 纯 CPU 不可用",
                Note = "质量档，中文语感更好"
            }
        };

        /// <summary>按文件名找到目录中的模型定义；找不到返回 null。</summary>
        public static LocalModelOption? FindByFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            return All.FirstOrDefault(m =>
                string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按部署记录判断是否支持信息提取；自定义模型默认按支持处理。</summary>
        public static bool SupportsMemoryExtraction(DeployedModel? model)
        {
            if (model == null)
            {
                return false;
            }

            return FindByFileName(model.FileName)?.SupportsMemoryExtraction ?? true;
        }

        /// <summary>按部署记录判断是否支持多条总结；1.7B 在调用点清单里标注为不可用。</summary>
        public static bool SupportsMemorySummarization(DeployedModel? model)
        {
            if (model == null)
            {
                return false;
            }

            return FindByFileName(model.FileName)?.SupportsMemorySummarization ?? true;
        }

        /// <summary>为当前硬件计算每个模型的运行状态，并选出推荐档（能流畅运行的最大模型）。</summary>
        public static IReadOnlyList<LocalModelOption> Recommend(HardwareInfo hardware)
        {
            var options = All.Select(m => new LocalModelOption
            {
                Name = m.Name,
                Parameters = m.Parameters,
                Quantization = m.Quantization,
                DownloadSizeBytes = m.DownloadSizeBytes,
                FileName = m.FileName,
                DownloadUrls = m.DownloadUrls,
                MinVramBytes = m.MinVramBytes,
                MinRamBytes = m.MinRamBytes,
                RecommendedVramBytes = m.RecommendedVramBytes,
                RecommendedRamBytes = m.RecommendedRamBytes,
                CpuFallbackAllowed = m.CpuFallbackAllowed,
                RequirementText = m.RequirementText,
                Note = m.Note,
                IsDefault = m.IsDefault,
                SupportsMemoryExtraction = m.SupportsMemoryExtraction,
                SupportsMemorySummarization = m.SupportsMemorySummarization
            }).ToList();

            foreach (var option in options)
            {
                var ramFits = hardware.TotalRamBytes >= option.MinRamBytes * Tolerance;
                var vramFits = hardware.DedicatedVideoMemoryBytes >= option.MinVramBytes * Tolerance;
                var cpuFits = option.CpuFallbackAllowed && ramFits;

                if (!ramFits || (!vramFits && !cpuFits))
                {
                    option.CanRun = false;
                    option.StatusText = StatusInsufficient;
                    continue;
                }

                option.CanRun = true;
                var smooth = vramFits &&
                             hardware.DedicatedVideoMemoryBytes >= option.RecommendedVramBytes * Tolerance &&
                             hardware.TotalRamBytes >= option.RecommendedRamBytes * Tolerance;

                option.StatusText = smooth ? StatusSmooth : StatusLow;
            }

            var recommended = options.LastOrDefault(o => o.StatusText == StatusSmooth)
                              ?? options.LastOrDefault(o => o.CanRun);

            if (recommended != null)
            {
                recommended.IsRecommended = true;
            }

            return options;
        }
    }
}
