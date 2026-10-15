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
                           "应用名称就按主人说的来即可。" +
                           "如果本工具返回“没有找到，你是不是想开这些之一”并给出候选名称，" +
                           "请结合你自己的判断从中挑出最可能的那个，用它的确切名称再调用一次本工具；" +
                           "都确定不了就如实告诉主人你没找到。";
                }

                var names = settings.AllowedApps
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var list = names.Count > 0 ? string.Join("、", names) : "（暂无）";
                return "帮主人打开电脑上的应用程序。" +
                       "只有当主人明确要求你打开某个应用时才调用。" +
                       $"目前只允许打开这些应用：{list}。" +
                       "主人用简称称呼名单里的应用时（例如把 Visual Studio Code 说成 vsc），" +
                       "直接按主人说的叫法调用即可，我会在名单内帮你匹配。" +
                       "如果主人想开的应用确实不在这个名单里，就如实告诉他需要先去设置里允许，不要编造应用名。";
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
            var allowed = settings.AllowedApps;
            if (!fullAccess && allowed.Count == 0)
            {
                return Task.FromResult("主人还没有允许任何应用，请先在设置里添加想让我打开的应用。");
            }

            // 1) 白名单优先（两种模式都先看白名单）：名单内支持简称/缩写匹配，且绝不越出名单
            if (allowed.Count > 0)
            {
                var whitelistMatch = AppResolveService.MatchCandidates(name, BuildWhitelistCandidates(allowed));
                if (whitelistMatch.Path != null)
                {
                    if (!string.Equals(whitelistMatch.MatchedName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info($"[打开应用] 名称「{name}」未精确匹配，按白名单近似猜中「{whitelistMatch.MatchedName}」并打开");
                    }

                    return Launch(whitelistMatch.Path, whitelistMatch.MatchedName ?? name);
                }

                // 白名单模式：只在名单内找，找不到就到此为止（绝不外扩）
                if (!fullAccess)
                {
                    if (whitelistMatch.Candidates.Count > 0)
                    {
                        var whitelistList = string.Join("、", whitelistMatch.Candidates);
                        Log.Info($"[打开应用] 名称「{name}」在白名单内无法确定，提示候选：{whitelistList}");
                        return Task.FromResult(
                            $"主人允许的应用里没有叫「{name}」的。你是不是想开这些之一：{whitelistList}？" +
                            "如果其中有一个就是，请用它确切的名字再调用一次本工具；都不是就如实告诉主人需要先去设置里允许。");
                    }

                    return Task.FromResult($"主人没有允许打开「{name}」，我不能打开它。");
                }
            }

            // 2) 完全访问模式：本地先自己判断全机应用池——精确匹配，或“唯一且高置信”的近似猜测
            var match = AppResolveService.Match(name);
            if (match.Path != null)
            {
                if (!string.Equals(match.MatchedName, name, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"[打开应用] 名称「{name}」未精确匹配，本地按近似猜中「{match.MatchedName}」并直接打开");
                }

                return Launch(match.Path, match.MatchedName ?? name);
            }

            // 4) 本地猜不出来（只有弱匹配或出现并列）：把候选交给模型兜底挑选
            if (match.Candidates.Count > 0)
            {
                var list = string.Join("、", match.Candidates);
                Log.Info($"[打开应用] 名称「{name}」本地无法确定，交给模型判断候选：{list}");
                return Task.FromResult(
                    $"本地没能直接确定叫「{name}」的程序。相关程序有这些：{list}。" +
                    "请结合你自己的知识判断：如果主人说的就是这个软件、而它常被叫作清单里的某个名字，" +
                    "就直接用那个名字再调用一次本工具；" +
                    "如果清单里都不像，但你确定这个软件在本机常用的名称或可执行名（例如 VS Code 的可执行名是 code），" +
                    "也可以直接用那个名字再调一次本工具；实在判断不了，才如实告诉主人你没找到。");
            }

            // 5) 兜底：交给 ShellExecute，让 Windows 自行按 PATH / 已注册项解析
            return TryShellExecute(name);
        }

        /// <summary>
        /// 把白名单条目展开成候选：每条同时提供“显示名”与“可执行文件名（去扩展名）”两个候选名，
        /// 这样主人说“code”或“Visual Studio Code”都能命中同一条目，且始终不越出白名单。
        /// </summary>
        private static IReadOnlyList<AppResolveService.Candidate> BuildWhitelistCandidates(IReadOnlyList<AllowedAppEntry> allowed)
        {
            var list = new List<AppResolveService.Candidate>(allowed.Count * 2);
            foreach (var entry in allowed)
            {
                if (string.IsNullOrWhiteSpace(entry.ExecutablePath))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.Name))
                {
                    list.Add(new AppResolveService.Candidate(entry.Name, entry.ExecutablePath));
                }

                var fileName = Path.GetFileNameWithoutExtension(entry.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    !string.Equals(fileName, entry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new AppResolveService.Candidate(fileName, entry.ExecutablePath));
                }
            }

            return list;
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
