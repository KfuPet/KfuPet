using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using KfuPet.Models;
using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    /// <summary>
    /// 视觉服务：截取主显示器画面并交给多模态模型做定向回答。
    /// 截图只在内存中处理、不落盘；是否允许看屏幕由 SettingsService.VisionEnabled 总开关控制。
    /// </summary>
    internal class VisionService : ICommandService
    {
        /// <summary>发给模型前缩放到的最长边像素，用于控制上传体积与图像 token。</summary>
        private const int MaxImageLongSide = 1024;

        /// <summary>JPEG 压缩质量，兼顾清晰度与体积。</summary>
        private const long JpegQuality = 80;

        /// <summary>视觉问答的系统提示：只回答画面里能看到的内容，避免模型凭空编造。</summary>
        private const string SystemPrompt =
            "你正在替桌宠观察主人当前的电脑屏幕。只根据截图内容回答，不要编造画面里没有的信息；" +
            "如果画面里看不到相关内容，直接说明没看到。回答简短，一到三句话。";

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

        /// <summary>截取主显示器画面，并让视觉模型回答指定问题。</summary>
        /// <param name="model">用于视觉问答的远端模型。</param>
        /// <param name="question">希望从画面中了解的问题。</param>
        /// <returns>模型针对画面的文字回答。</returns>
        internal async Task<string> LookAsync(ModelConfig model, string question)
        {
            var imageBytes = await Task.Run(CapturePrimaryScreenJpeg);
            Log.Debug($"[视觉] 截图完成：{imageBytes.Length / 1024} KB");

            var reply = await _chatService.SendVisionAsync(
                model, SystemPrompt, question, imageBytes, "image/jpeg",
                ModelCallKind.Vision, TokenUsageCategory.Vision);

            return string.IsNullOrWhiteSpace(reply) ? "没有看清屏幕上的内容。" : reply;
        }

        /// <summary>截取主显示器并编码为最长边不超过 1024 的 JPEG；需在后台线程调用。</summary>
        private static byte[] CapturePrimaryScreenJpeg()
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

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
    }
}
