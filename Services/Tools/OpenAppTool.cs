using System.Diagnostics;
using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 打开应用工具：主人明确要求打开某个应用时由模型调用。
    /// 白名单模式下只能打开用户在设置里允许的应用；完全访问模式下忽略白名单，可打开任意应用。
    /// 路径永远由本地解析，模型只提供名称。
    /// </summary>
    internal class OpenAppTool : ITool
    {
        /// <summary>工具名称（供注册与筛选逻辑引用）。</summary>
        public const string ToolName = "open_app";

        public string Name => ToolName;

        /// <summary>
        /// 工具描述随设置动态生成：白名单模式下把“已允许的应用名”直接列给模型，
        /// 避免模型不知道合法名称而猜错；完全访问模式下说明可按主人说的名称来。
        /// （每次请求都会重新求值，改动设置后立即生效。）
        /// </summary>
        public string Description
        {
            get
            {
                var settings = SettingsService.Instance;
                if (settings.OpenAppFullAccess)
                {
                    return "帮主人打开电脑上的应用程序。" +
                           "只有当主人明确要求你打开某个应用（例如“帮我打开微信”）时才调用。" +
                           "应用名称就按主人说的来即可，不用去猜别的名字。";
                }

                var names = settings.AllowedApps
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var list = names.Count > 0 ? string.Join("、", names) : "（暂无）";
                return "帮主人打开电脑上的应用程序。" +
                       "只有当主人明确要求你打开某个应用时才调用。" +
                       $"目前只允许打开这些应用：{list}。" +
                       "如果主人想开的应用不在这个名单里，就如实告诉他需要先去设置里允许，不要猜测或编造应用名。";
            }
        }

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "要打开的应用名称；白名单模式下必须是已允许的名称，完全访问模式下按主人说的名称即可"
                }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """;

        public Task<string> ExecuteAsync(string argumentsJson)
        {
            var name = ReadName(argumentsJson);
            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult("没有提供要打开的应用名称。");
            }

            var settings = SettingsService.Instance;
            if (!settings.OpenAppEnabled)
            {
                return Task.FromResult("主人还没有开启“帮你打开应用”的功能，我不能打开程序。");
            }

            var fullAccess = settings.OpenAppFullAccess;
            if (!fullAccess && settings.AllowedApps.Count == 0)
            {
                return Task.FromResult("主人还没有允许任何应用，请先在设置里添加想让我打开的应用。");
            }

            // 1) 白名单优先：按显示名或文件名（去扩展名）忽略大小写地匹配
            var entry = MatchAllowed(name, settings.AllowedApps);
            if (entry != null)
            {
                return Launch(entry.ExecutablePath, entry.Name);
            }

            // 2) 白名单模式下，名单外的名称一律拒绝
            if (!fullAccess)
            {
                return Task.FromResult($"主人没有允许打开「{name}」，我不能打开它。");
            }

            // 3) 完全访问模式：本地解析名称 → 路径（注册表 App Paths / 开始菜单快捷方式）
            var resolved = AppResolveService.Resolve(name);
            if (resolved != null)
            {
                return Launch(resolved, name);
            }

            // 4) 兜底：交给 ShellExecute，让 Windows 自行按 PATH / 已注册项解析
            return TryShellExecute(name);
        }

        /// <summary>在白名单里按“显示名”或“文件名（去扩展名）”忽略大小写地查找条目；未命中返回 null。</summary>
        private static AllowedAppEntry? MatchAllowed(string name, IReadOnlyList<AllowedAppEntry> allowed)
        {
            var exact = allowed.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            return allowed.FirstOrDefault(e => string.Equals(
                Path.GetFileNameWithoutExtension(e.ExecutablePath), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>启动指定路径的可执行文件；路径不存在或启动失败时返回可读的提示。</summary>
        private static Task<string> Launch(string path, string displayName)
        {
            if (!File.Exists(path))
            {
                Log.Warning($"[打开应用] 找不到可执行文件：{displayName}");
                return Task.FromResult($"「{displayName}」的程序路径找不到了，请重新在设置里选择。");
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Log.Info($"[打开应用] 已按主人要求打开：{displayName}");
                return Task.FromResult($"已经帮主人打开「{displayName}」了。");
            }
            catch (Exception ex)
            {
                Log.Warning($"[打开应用] 启动失败：{ex.Message}");
                return Task.FromResult($"打开「{displayName}」失败了：{ex.Message}");
            }
        }

        /// <summary>兜底：把原始名称交给 ShellExecute 解析并启动（完全访问模式专用）。</summary>
        private static Task<string> TryShellExecute(string name)
        {
            try
            {
                Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
                Log.Info($"[打开应用] 交由系统解析并打开：{name}");
                return Task.FromResult($"已经帮主人打开「{name}」了。");
            }
            catch (Exception ex)
            {
                Log.Warning($"[打开应用] 未找到可打开的程序：{name}（{ex.Message}）");
                return Task.FromResult($"没有找到名为「{name}」的程序，可以换个名字，或先去设置里允许它。");
            }
        }

        /// <summary>从工具参数里取出应用名称；未提供或解析失败返回空串。</summary>
        private static string ReadName(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.TryGetProperty("name", out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[打开应用] 工具参数解析失败：{ex.Message}");
            }

            return string.Empty;
        }
    }
}
