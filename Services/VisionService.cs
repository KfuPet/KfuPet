using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using KfuPet.Models;
using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    /// <summary>
    /// 视觉服务：截取前台窗口或主屏画面，交给多模态模型做定向回答。
    /// 截图只在内存中处理、不落盘；是否允许看屏幕由 SettingsService.VisionEnabled 总开关控制。
    /// </summary>
    internal class VisionService : ICommandService
    {
        /// <summary>发给模型前缩放到的最长边像素，用于控制上传体积与图像 token。</summary>
        private const int MaxImageLongSide = 1024;

        /// <summary>JPEG 压缩质量，兼顾清晰度与体积。</summary>
        private const long JpegQuality = 80;

        /// <summary>可截取窗口的最小边长（像素）：过滤掉工具提示、浮动小面板等无意义窗口。</summary>
        private const int MinWindowSide = 240;

        /// <summary>GetWindow 的 GW_HWNDNEXT：沿 Z 序取下一个窗口。</summary>
        private const uint GW_HWNDNEXT = 2;

        /// <summary>视觉问答的系统提示：只回答画面里能看到的内容，避免模型凭空编造。</summary>
        private const string SystemPrompt =
            "你正在替桌宠观察主人当前的电脑屏幕。画面里可能出现一个二次元角色，那是你自己（桌宠），" +
            "属于叠加在屏幕上的界面，不是主人正在浏览的内容：除非主人明确问起，否则忽略它、不要描述它。" +
            "只根据截图内容回答，不要编造画面里没有的信息；" +
            "如果画面里看不到相关内容，直接说明没看到。回答简短，一到三句话。";

        /// <summary>进程名命中即拒绝截图的隐私类应用（密码管理、远程控制、Windows 凭据界面）。</summary>
        private static readonly HashSet<string> SensitiveProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "1password", "keepass", "keepassxc", "bitwarden", "dashlane", "lastpass", "enpass",
            "mstsc", "credentialuibroker", "logonui",
            "teamviewer", "anydesk", "rustdesk", "parsec"
        };

        /// <summary>桌面壳层窗口类名：这些窗口不是用户正在使用的应用。</summary>
        private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };

        private readonly ChatService _chatService;

        /// <summary>用聊天服务构造：视觉问答复用统一的远端请求与 token 统计。</summary>
        /// <param name="chatService">共享的聊天服务。</param>
        public VisionService(ChatService chatService)
        {
            _chatService = chatService;
        }

        public string ServiceName => "vision";

        public CommandResponse Execute(string action, Dictionary<string, object>? parameters)
        {
            // 截图与模型调用是异步流程且可能较慢，暂不通过 IPC 命令通道承载
            return CommandResponse.Fail("视觉服务通过对话工具调用，暂不支持外部命令");
        }

        /// <summary>
        /// 按当前设置截取画面并返回 JPEG 字节（在后台线程执行）。
        /// 命中隐私黑名单或截图失败时抛出 <see cref="InvalidOperationException"/>，由调用方转述原因。
        /// </summary>
        internal async Task<byte[]> CaptureScreenJpegAsync()
        {
            var foregroundOnly = SettingsService.Instance.VisionForegroundOnly;
            var capture = await Task.Run(() => CaptureJpeg(foregroundOnly));
            if (capture.Bytes == null)
            {
                throw new InvalidOperationException(capture.Message ?? "截图失败");
            }

            Log.Debug($"[视觉] 截图完成：{capture.Bytes.Length / 1024} KB（{(foregroundOnly ? "前台窗口" : "整屏")}）");
            return capture.Bytes;
        }

        /// <summary>让视觉模型回答与截图相关的问题。</summary>
        /// <param name="model">用于视觉问答的远端模型。</param>
        /// <param name="question">希望从画面中了解的问题。</param>
        /// <param name="imageBytes">已截取的 JPEG 字节。</param>
        internal async Task<string> AskAsync(ModelConfig model, string question, byte[] imageBytes)
        {
            var reply = await _chatService.SendVisionAsync(
                model, SystemPrompt, question, imageBytes, "image/jpeg",
                ModelCallKind.Vision, TokenUsageCategory.Vision);

            return string.IsNullOrWhiteSpace(reply) ? "没有看清屏幕上的内容。" : reply;
        }

        /// <summary>
        /// 按截图范围抓取画面：仅前台窗口时截取活动窗口，整屏时截取主显示器。
        /// 前台是本进程、最小化或拿不到有效窗口时自动退回整屏。
        /// </summary>
        private static (byte[]? Bytes, string? Message) CaptureJpeg(bool foregroundOnly)
        {
            var target = FindCaptureTarget();

            // 隐私黑名单在两种范围下都生效：整屏同样会把敏感内容发给模型
            if (target != null && IsSensitiveProcess(target.ProcessName))
            {
                Log.Debug($"[视觉] 当前窗口属于隐私类应用（{target.ProcessName}），放弃截图");
                return (null, "当前窗口涉及隐私内容，我不方便看。");
            }

            var bounds = foregroundOnly && target != null
                ? target.Bounds
                : GetPrimaryScreenBounds();

            return (CaptureBoundsJpeg(bounds), null);
        }

        /// <summary>截取指定屏幕区域并编码为最长边不超过 1024 的 JPEG；需在后台线程调用。</summary>
        private static byte[] CaptureBoundsJpeg(Rectangle bounds)
        {
            using var screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(screenshot))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            var scale = Math.Min(
                1.0,
                MaxImageLongSide / (double)Math.Max(1, Math.Max(screenshot.Width, screenshot.Height)));

            Bitmap? scaled = null;
            try
            {
                var output = screenshot;
                if (scale < 1.0)
                {
                    scaled = new Bitmap(
                        Math.Max(1, (int)(screenshot.Width * scale)),
                        Math.Max(1, (int)(screenshot.Height * scale)),
                        PixelFormat.Format24bppRgb);

                    using var graphics = Graphics.FromImage(scaled);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(screenshot, 0, 0, scaled.Width, scaled.Height);
                    output = scaled;
                }

                using var stream = new MemoryStream();
                var encoder = ImageCodecInfo.GetImageEncoders()
                    .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                output.Save(stream, encoder, parameters);
                return stream.ToArray();
            }
            finally
            {
                scaled?.Dispose();
            }
        }

        /// <summary>
        /// 选择截图目标窗口：优先前台窗口；前台是桌宠自身、最小化或尺寸过小时，
        /// 沿 Z 序找最近一个外部可见窗口，避免用户与桌宠交互时只截到桌宠自己。
        /// </summary>
        private static CaptureTarget? FindCaptureTarget()
        {
            var foreground = GetForegroundWindow();
            var target = TryBuildTarget(foreground);
            if (target != null)
            {
                return target;
            }

            var window = GetTopWindow(IntPtr.Zero);
            while (window != IntPtr.Zero)
            {
                target = TryBuildTarget(window);
                if (target != null)
                {
                    return target;
                }

                window = GetWindow(window, GW_HWNDNEXT);
            }

            return null;
        }

        /// <summary>检查单个窗口是否可作为截图目标，可则返回裁剪到虚拟屏幕后的范围。</summary>
        private static CaptureTarget? TryBuildTarget(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window))
            {
                return null;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                return null;
            }

            if (ShellWindowClasses.Contains(GetWindowClassName(window)))
            {
                return null;
            }

            if (!GetWindowRect(window, out var rect))
            {
                return null;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < MinWindowSide || height < MinWindowSide)
            {
                return null;
            }

            var bounds = Rectangle.Intersect(
                new Rectangle(rect.Left, rect.Top, width, height),
                SystemInformation.VirtualScreen);
            if (bounds.Width < MinWindowSide || bounds.Height < MinWindowSide)
            {
                return null;
            }

            return new CaptureTarget
            {
                Bounds = bounds,
                ProcessName = GetProcessName(processId)
            };
        }

        /// <summary>主显示器范围；拿不到时退回 1920×1080。</summary>
        private static Rectangle GetPrimaryScreenBounds()
            => Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        /// <summary>读取窗口所属进程名；进程已退出或受权限保护时返回空串。</summary>
        private static string GetProcessName(uint processId)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch (Exception ex)
            {
                Log.Debug($"[视觉] 读取进程 {processId} 失败：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>判断进程是否为隐私类应用；进程名可带或不带 .exe。</summary>
        private static bool IsSensitiveProcess(string processName)
        {
            var name = processName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }

            return !string.IsNullOrEmpty(name) && SensitiveProcessNames.Contains(name);
        }

        private static string GetWindowClassName(IntPtr window)
        {
            var builder = new StringBuilder(256);
            return GetClassName(window, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
        }

        /// <summary>截图目标：裁剪后的屏幕范围与所属进程名。</summary>
        private sealed class CaptureTarget
        {
            public Rectangle Bounds { get; init; }

            public string ProcessName { get; init; } = string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
