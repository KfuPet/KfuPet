using System.Text.Json;
using System.Windows.Media;
using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    /// <summary>
    /// 窗口调试服务，仅供开发者工具通过 IPC 使用。
    /// 支持自定义桌宠窗口的背景色，便于检查角色图片的边缘与透明通道；颜色不做持久化，
    /// 关闭开发者模式后由主窗口恢复透明背景。
    /// </summary>
    internal class WindowService : ICommandService
    {
        public string ServiceName => "window";

        private Color _backgroundColor = Colors.Transparent;

        /// <summary>
        /// 背景色变化时触发，参数为新的窗口背景色（<see cref="Colors.Transparent"/> 表示恢复透明）。
        /// 命令经 IPC 在 UI 线程分发，订阅方可直接更新界面。
        /// </summary>
        public event EventHandler<Color>? BackgroundColorChanged;

        public CommandResponse Execute(string action, Dictionary<string, object>? parameters)
        {
            switch (action.ToLowerInvariant())
            {
                case "setbackgroundcolor":
                    if (!TryParseColor(Str(parameters, "color"), out var color))
                    {
                        return CommandResponse.Fail("Invalid color. Use #AARRGGBB, #RRGGBB or Transparent");
                    }
                    SetBackgroundColor(color);
                    return CommandResponse.Ok(true);

                case "getbackgroundcolor":
                    return CommandResponse.Ok(FormatColor(_backgroundColor));

                case "resetbackgroundcolor":
                    ResetBackground();
                    return CommandResponse.Ok(true);

                default:
                    return CommandResponse.Fail($"Unknown action: {action}");
            }
        }

        /// <summary>
        /// 恢复透明背景（关闭开发者模式时由主窗口调用）。
        /// </summary>
        public void ResetBackground()
        {
            SetBackgroundColor(Colors.Transparent);
        }

        private void SetBackgroundColor(Color color)
        {
            if (_backgroundColor == color)
            {
                return;
            }

            _backgroundColor = color;
            Log.Debug($"[窗口] 开发者工具设置背景色：{FormatColor(color)}");
            BackgroundColorChanged?.Invoke(this, color);
        }

        /// <summary>
        /// 把颜色格式化为 #AARRGGBB；完全不透明以外仍保留 Alpha，便于半透明调试。
        /// </summary>
        private static string FormatColor(Color color)
        {
            return color.A == 0 ? "Transparent" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// 解析开发者工具传入的颜色，仅接受十六进制颜色（#RGB / #ARGB / #RRGGBB / #AARRGGBB）或 Transparent。
        /// </summary>
        private static bool TryParseColor(string? text, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var value = text.Trim();
            if (string.Equals(value, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!value.StartsWith('#'))
            {
                return false;
            }

            try
            {
                color = (Color)ColorConverter.ConvertFromString(value)!;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string? Str(Dictionary<string, object>? parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value is JsonElement element ? element.GetString() : value.ToString();
        }
    }
}
