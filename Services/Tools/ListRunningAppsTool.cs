namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 查看正在使用的应用工具：主人问起“我开了哪些应用”时由模型调用，
    /// 返回任务栏上正在运行的应用与当前前台应用。受“读取正在使用的应用”授权开关控制。
    /// </summary>
    internal class ListRunningAppsTool : ITool
    {
        /// <summary>工具名称（供注册与筛选逻辑引用）。</summary>
        public const string ToolName = "list_running_apps";

        public string Name => ToolName;

        public string Description =>
            "查看主人电脑任务栏上现在开着哪些应用，以及主人正在使用哪一个。" +
            "只有当主人明确问起“我开了哪些应用”“我在用什么软件”“现在开着什么”之类的问题时才调用。" +
            "普通闲聊或主人没有问起应用时不要调用。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """;

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            if (!SettingsService.Instance.ForegroundAppContextEnabled)
            {
                return "主人还没有允许我查看正在使用的应用，我读不到任务栏上的应用。";
            }

            // 枚举窗口与读取进程信息放到后台，避免阻塞界面
            var apps = await Task.Run(ForegroundAppService.TryGetRunningApps);
            if (apps.Count == 0)
            {
                return "没有读取到任务栏上打开的应用。";
            }

            var list = string.Join("、", apps.Select(a => a.Name));
            string? foregroundName = null;
            foreach (var app in apps)
            {
                if (app.IsForeground)
                {
                    foregroundName = app.Name;
                    break;
                }
            }

            return string.IsNullOrEmpty(foregroundName)
                ? $"当前任务栏上打开的应用有：{list}（共 {apps.Count} 个）。"
                : $"当前任务栏上打开的应用有：{list}（共 {apps.Count} 个），主人正在使用「{foregroundName}」。";
        }
    }
}
