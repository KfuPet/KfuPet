using System.Runtime.InteropServices;
using Microsoft.Win32;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>读取本机 CPU / 内存 / 显卡信息。显存走注册表，避开 WMI AdapterRAM 在 4GB 以上被截断的问题。</summary>
    internal static class HardwareProbe
    {
        private const string GpuClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

        /// <summary>读取本机硬件摘要，失败项以“未识别”与 0 兜底，不阻断后续流程。</summary>
        public static HardwareInfo Detect()
        {
            var (gpuName, vram) = ReadGpu();
            return new HardwareInfo(
                ReadCpuName(),
                Environment.ProcessorCount,
                ReadTotalRam(),
                gpuName,
                vram);
        }

        /// <summary>枚举显卡类注册表项，优先返回显存最大的独显；只有核显时显存按 0 处理。</summary>
        private static (string Name, long Vram) ReadGpu()
        {
            string? integratedName = null;

            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(GpuClassKey);
                if (classKey == null)
                {
                    return ("未识别到显卡", 0);
                }

                string? bestDedicatedName = null;
                long bestDedicatedVram = 0;

                foreach (var subName in classKey.GetSubKeyNames())
                {
                    if (subName.Length != 4 || !int.TryParse(subName, out _))
                    {
                        continue;
                    }

                    using var subKey = classKey.OpenSubKey(subName);
                    if (subKey?.GetValue("DriverDesc") is not string desc || string.IsNullOrWhiteSpace(desc))
                    {
                        continue;
                    }

                    if (desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!IsDedicated(desc))
                    {
                        integratedName ??= desc;
                        continue;
                    }

                    var memory = ReadQword(subKey.GetValue("HardwareInformation.qwMemorySize"));
                    if (memory > bestDedicatedVram)
                    {
                        bestDedicatedVram = memory;
                        bestDedicatedName = desc;
                    }
                }

                if (bestDedicatedName != null)
                {
                    return (bestDedicatedName, bestDedicatedVram);
                }
            }
            catch
            {
                // 注册表读取失败时退化为“未识别”，不阻断下载流程
            }

            return (integratedName ?? "未识别到显卡", 0);
        }

        /// <summary>按型号名判断是否为独显；核显 / 虚拟显卡不计入显存档位。</summary>
        private static bool IsDedicated(string name)
        {
            string[] dedicatedMarks = { "GeForce", "RTX", "GTX", "Quadro", "Radeon RX", "Radeon Pro", "Arc" };
            return dedicatedMarks.Any(mark => name.Contains(mark, StringComparison.OrdinalIgnoreCase));
        }

        private static long ReadQword(object? value)
        {
            return value switch
            {
                long l => l,
                int i => i,
                byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
                _ => 0
            };
        }

        private static string ReadCpuName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(CpuKey);
                return key?.GetValue("ProcessorNameString") as string ?? "未知处理器";
            }
            catch
            {
                return "未知处理器";
            }
        }

        private static long ReadTotalRam()
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
    }
}
