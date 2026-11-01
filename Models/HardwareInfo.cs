namespace KfuPet.Models
{
    /// <summary>本机硬件摘要：由 HardwareProbe 读取，用于本地模型推荐。</summary>
    public sealed record HardwareInfo(
        string CpuName,
        int LogicalCores,
        long TotalRamBytes,
        string GpuName,
        long DedicatedVideoMemoryBytes)
    {
        /// <summary>物理内存总量（GB）。</summary>
        public double RamGb => TotalRamBytes / 1024d / 1024d / 1024d;

        /// <summary>独显显存总量（GB）；无独显时为 0。</summary>
        public double VramGb => DedicatedVideoMemoryBytes / 1024d / 1024d / 1024d;

        /// <summary>是否识别到独显；核显或未识别时按无独显处理。</summary>
        public bool HasDedicatedGpu => DedicatedVideoMemoryBytes > 0;
    }
}
