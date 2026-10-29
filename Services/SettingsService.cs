using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>模型功能节省模式：控制触摸反应、更新通知文案与越界回正文案是否改用本地文案。</summary>
    internal enum SavingMode
    {
        /// <summary>关闭：不启用节省，各功能保持现状（由 AI 生成）。</summary>
        Off,

        /// <summary>节省：各功能改用本地文案，不再消耗模型请求。</summary>
        Saving,

        /// <summary>自定义：各功能分别控制是否使用 AI 生成。</summary>
        Custom
    }

    /// <summary>主动搭话频度档位：决定独处判定阈值、冷却时长与触发概率。</summary>
    internal enum ProactiveFrequency
    {
        /// <summary>低频：约 2–3 小时一句。</summary>
        Low,

        /// <summary>中频（默认）：约 1–1.5 小时一句。</summary>
        Medium,

        /// <summary>高频：约 30–40 分钟一句。</summary>
        High
    }

    /// <summary>
    /// 应用通用设置，统一读写缓存目录下的 Config\settings.json（默认 %AppData%\KfuPet，可在设置中自定义）。
    /// 目前包含主题偏好、开发者模式、骨骼调试线框、桌宠窗口位置、当前使用角色包、模型功能节省模式与主动搭话；后续通用设置继续在此扩展字段。
    /// </summary>
    internal class SettingsService
    {
        private static readonly string ConfigFilePath = Path.Combine(AppPaths.ConfigDirectory, "settings.json");

        private static readonly Lazy<SettingsService> LazyInstance = new(() => new SettingsService());

        /// <summary>全局共享实例（启动早期即在 App、主窗口与各服务间共享）。</summary>
        public static SettingsService Instance => LazyInstance.Value;

        /// <summary>主题偏好：true 深色，false 浅色，null 跟随系统。</summary>
        public bool? Theme { get; private set; }

        /// <summary>是否启用开发者模式。</summary>
        public bool DeveloperMode { get; private set; }

        /// <summary>是否显示骨骼调试线框。</summary>
        public bool DebugBones { get; private set; }

        /// <summary>桌宠窗口左上角 X 坐标（逻辑像素）；null 表示尚未记录，启动时居中。</summary>
        public double? WindowLeft { get; private set; }

        /// <summary>桌宠窗口左上角 Y 坐标（逻辑像素）；null 表示尚未记录，启动时居中。</summary>
        public double? WindowTop { get; private set; }

        /// <summary>当前使用的角色包标识（character.json 的 id，缺省时为文件夹名）；空串表示未选择过，按默认角色启动。</summary>
        public string ActiveCharacterId { get; private set; } = string.Empty;

        /// <summary>最近一次已展示过的公告标识；同一公告不再重复弹出。</summary>
        public string LastSeenAnnouncementId { get; private set; } = string.Empty;

        /// <summary>模型功能节省模式。</summary>
        public SavingMode ModelSavingMode { get; private set; }

        /// <summary>自定义模式下触摸反应是否使用 AI 生成；false 表示改用角色台词。</summary>
        public bool CustomTouchReactionUseAi { get; private set; }

        /// <summary>自定义模式下更新通知文案是否使用 AI 生成；false 表示改用固定文案。</summary>
        public bool CustomUpdateNotificationUseAi { get; private set; }

        /// <summary>自定义模式下越界回正文案是否使用 AI 生成；false 表示改用角色台词。</summary>
        public bool CustomSnapBackUseAi { get; private set; }

        /// <summary>自定义模式下对话后的情绪判断是否使用 AI；false 表示不做判断。</summary>
        public bool CustomEmotionJudgeUseAi { get; private set; }

        /// <summary>自定义模式下核心信息提取是否走触发词门控；false 表示每轮对话都提取。</summary>
        public bool CustomMemoryExtractionGated { get; private set; }

        /// <summary>触摸反应是否使用 AI 生成：关闭模式保持现状；节省模式始终不用；自定义模式按细分开关。</summary>
        public bool TouchReactionUseAi => ModelSavingMode switch
        {
            SavingMode.Saving => false,
            SavingMode.Custom => CustomTouchReactionUseAi,
            _ => true
        };

        /// <summary>更新通知文案是否使用 AI 生成：关闭模式保持现状；节省模式始终不用；自定义模式按细分开关。</summary>
        public bool UpdateNotificationUseAi => ModelSavingMode switch
        {
            SavingMode.Saving => false,
            SavingMode.Custom => CustomUpdateNotificationUseAi,
            _ => true
        };

        /// <summary>越界回正文案是否使用 AI 生成：关闭模式保持现状；节省模式始终不用；自定义模式按细分开关。</summary>
        public bool SnapBackUseAi => ModelSavingMode switch
        {
            SavingMode.Saving => false,
            SavingMode.Custom => CustomSnapBackUseAi,
            _ => true
        };

        /// <summary>对话后的情绪判断是否使用 AI：关闭模式保持现状；节省模式始终不用；自定义模式按细分开关。</summary>
        public bool EmotionJudgeUseAi => ModelSavingMode switch
        {
            SavingMode.Saving => false,
            SavingMode.Custom => CustomEmotionJudgeUseAi,
            _ => true
        };

        /// <summary>核心信息提取是否走触发词门控：关闭模式每轮都提取；节省模式始终门控；自定义模式按细分开关。</summary>
        public bool MemoryExtractionGated => ModelSavingMode switch
        {
            SavingMode.Saving => true,
            SavingMode.Custom => CustomMemoryExtractionGated,
            _ => false
        };

        /// <summary>是否启用主动搭话（总开关，默认开启）。</summary>
        public bool ProactiveChatEnabled { get; private set; }

        /// <summary>主动搭话频度档位（默认中频）。</summary>
        public ProactiveFrequency ProactiveChatFrequency { get; private set; }

        /// <summary>是否启用安静时段（默认开启）。</summary>
        public bool QuietHoursEnabled { get; private set; }

        /// <summary>安静时段开始小时（0–23），默认 23 点。</summary>
        public int QuietHoursStart { get; private set; }

        /// <summary>安静时段结束小时（0–23），默认次日 8 点；起止相同视为未设置（该设置不生效）。</summary>
        public int QuietHoursEnd { get; private set; }

        /// <summary>是否启用独处搭话（在电脑前但久未互动）。</summary>
        public bool ProactiveIdleEnabled { get; private set; }

        /// <summary>是否启用欢迎回来（离开较久后回到电脑前）。</summary>
        public bool ProactiveWelcomeBackEnabled { get; private set; }

        /// <summary>是否启用定时问候（早上 / 晚上各一次）。</summary>
        public bool ProactiveGreetingEnabled { get; private set; }

        /// <summary>自定义模式下主动搭话是否使用 AI 生成；false 表示改用角色台词。</summary>
        public bool CustomProactiveChatUseAi { get; private set; }

        /// <summary>主动搭话是否使用 AI 生成：关闭模式保持现状；节省模式始终不用；自定义模式按细分开关。</summary>
        public bool ProactiveChatUseAi => ModelSavingMode switch
        {
            SavingMode.Saving => false,
            SavingMode.Custom => CustomProactiveChatUseAi,
            _ => true
        };

        /// <summary>是否允许读取正在使用的应用（前台应用与任务栏应用，默认关闭）：
        /// 开启后主动搭话会注入应用语境，模型也可调用查看工具。</summary>
        public bool ForegroundAppContextEnabled { get; private set; }

        /// <summary>是否允许模型帮忙打开应用（总开关，默认关闭）。</summary>
        public bool OpenAppEnabled { get; private set; }

        /// <summary>允许模型打开的应用白名单（默认空；为空时模型无法打开任何程序）。</summary>
        public IReadOnlyList<AllowedAppEntry> AllowedApps { get; private set; } = Array.Empty<AllowedAppEntry>();

        /// <summary>
        /// 完全访问：开启后忽略白名单，允许模型打开任意应用（含系统工具）。
        /// 默认关闭；置为 true 需在界面经用户红色警示确认。
        /// </summary>
        public bool OpenAppFullAccess { get; private set; }

        private SettingsService()
        {
            ApplyDefaults();
            Load();
        }

        /// <summary>
        /// 恢复初始状态：各项设置回到默认值，并删除磁盘上的配置文件；
        /// 之后的新改动会重新生成配置文件。
        /// </summary>
        public void ResetToDefaults()
        {
            ApplyDefaults();

            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                }
            }
            catch (Exception ex)
            {
                // 删除失败不影响内存中的复位
                Log.Warning($"[设置] 配置文件删除失败：{ex.Message}");
            }

            Log.Info("[设置] 已恢复初始设置");
        }

        /// <summary>把各项设置还原为默认值（构造与“恢复初始状态”共用，默认值只在此处维护）。</summary>
        private void ApplyDefaults()
        {
            Theme = null;
            DeveloperMode = false;
            DebugBones = false;
            WindowLeft = null;
            WindowTop = null;
            ActiveCharacterId = string.Empty;
            LastSeenAnnouncementId = string.Empty;
            ModelSavingMode = SavingMode.Off;
            CustomTouchReactionUseAi = true;
            CustomUpdateNotificationUseAi = true;
            CustomSnapBackUseAi = true;
            CustomEmotionJudgeUseAi = true;
            CustomMemoryExtractionGated = false;
            ProactiveChatEnabled = true;
            ProactiveChatFrequency = ProactiveFrequency.Medium;
            QuietHoursEnabled = true;
            QuietHoursStart = 23;
            QuietHoursEnd = 8;
            ProactiveIdleEnabled = true;
            ProactiveWelcomeBackEnabled = true;
            ProactiveGreetingEnabled = true;
            CustomProactiveChatUseAi = true;
            ForegroundAppContextEnabled = false;
            OpenAppEnabled = false;
            AllowedApps = Array.Empty<AllowedAppEntry>();
            OpenAppFullAccess = false;
        }

        /// <summary>保存用户的外观选择：true 深色，false 浅色，null 跟随系统。</summary>
        public void SetTheme(bool? isDark)
        {
            Theme = isDark;
            Save();
        }

        /// <summary>保存开发者模式开关状态。</summary>
        public void SetDeveloperMode(bool enabled)
        {
            DeveloperMode = enabled;
            Save();
        }

        /// <summary>保存骨骼调试线框开关状态。</summary>
        public void SetDebugBones(bool show)
        {
            DebugBones = show;
            Save();
        }

        /// <summary>保存桌宠窗口位置（逻辑像素）。</summary>
        public void SetWindowPosition(double left, double top)
        {
            WindowLeft = left;
            WindowTop = top;
            Save();
        }

        /// <summary>保存当前使用的角色包标识（角色模型窗口「使用」按钮写入）。</summary>
        public void SetActiveCharacterId(string characterId)
        {
            ActiveCharacterId = characterId?.Trim() ?? string.Empty;
            Save();
        }

        /// <summary>记录已展示过的公告标识，避免同一公告每次启动都弹出。</summary>
        public void SetLastSeenAnnouncementId(string announcementId)
        {
            LastSeenAnnouncementId = announcementId?.Trim() ?? string.Empty;
            Save();
        }

        /// <summary>保存模型功能节省模式。</summary>
        public void SetModelSavingMode(SavingMode mode)
        {
            ModelSavingMode = mode;
            Save();
        }

        /// <summary>保存自定义模式下触摸反应是否使用 AI 生成。</summary>
        public void SetCustomTouchReactionUseAi(bool useAi)
        {
            CustomTouchReactionUseAi = useAi;
            Save();
        }

        /// <summary>保存自定义模式下更新通知文案是否使用 AI 生成。</summary>
        public void SetCustomUpdateNotificationUseAi(bool useAi)
        {
            CustomUpdateNotificationUseAi = useAi;
            Save();
        }

        /// <summary>保存自定义模式下越界回正文案是否使用 AI 生成。</summary>
        public void SetCustomSnapBackUseAi(bool useAi)
        {
            CustomSnapBackUseAi = useAi;
            Save();
        }

        /// <summary>保存自定义模式下对话后的情绪判断是否使用 AI。</summary>
        public void SetCustomEmotionJudgeUseAi(bool useAi)
        {
            CustomEmotionJudgeUseAi = useAi;
            Save();
        }

        /// <summary>保存自定义模式下核心信息提取是否走触发词门控。</summary>
        public void SetCustomMemoryExtractionGated(bool gated)
        {
            CustomMemoryExtractionGated = gated;
            Save();
        }

        /// <summary>保存主动搭话总开关状态。</summary>
        public void SetProactiveChatEnabled(bool enabled)
        {
            ProactiveChatEnabled = enabled;
            Save();
        }

        /// <summary>保存主动搭话频度档位。</summary>
        public void SetProactiveChatFrequency(ProactiveFrequency frequency)
        {
            ProactiveChatFrequency = frequency;
            Save();
        }

        /// <summary>保存安静时段的开始与结束小时（0–23）。</summary>
        public void SetQuietHours(int startHour, int endHour)
        {
            QuietHoursStart = Math.Clamp(startHour, 0, 23);
            QuietHoursEnd = Math.Clamp(endHour, 0, 23);
            Save();
        }

        /// <summary>保存安静时段开关状态。</summary>
        public void SetQuietHoursEnabled(bool enabled)
        {
            QuietHoursEnabled = enabled;
            Save();
        }

        /// <summary>保存独处搭话开关状态。</summary>
        public void SetProactiveIdleEnabled(bool enabled)
        {
            ProactiveIdleEnabled = enabled;
            Save();
        }

        /// <summary>保存欢迎回来开关状态。</summary>
        public void SetProactiveWelcomeBackEnabled(bool enabled)
        {
            ProactiveWelcomeBackEnabled = enabled;
            Save();
        }

        /// <summary>保存定时问候开关状态。</summary>
        public void SetProactiveGreetingEnabled(bool enabled)
        {
            ProactiveGreetingEnabled = enabled;
            Save();
        }

        /// <summary>保存自定义模式下主动搭话是否使用 AI 生成。</summary>
        public void SetCustomProactiveChatUseAi(bool useAi)
        {
            CustomProactiveChatUseAi = useAi;
            Save();
        }

        /// <summary>保存“读取正在使用的应用”授权开关状态。</summary>
        public void SetForegroundAppContextEnabled(bool enabled)
        {
            ForegroundAppContextEnabled = enabled;
            Save();
        }

        /// <summary>保存“允许模型帮忙打开应用”总开关状态。</summary>
        public void SetOpenAppEnabled(bool enabled)
        {
            OpenAppEnabled = enabled;
            Save();
        }

        /// <summary>保存允许打开的应用白名单（复制一份再存，避免界面直接改内存对象绕过落盘）。</summary>
        public void SetAllowedApps(IReadOnlyList<AllowedAppEntry> apps)
        {
            AllowedApps = apps.ToList();
            Save();
        }

        /// <summary>保存“完全访问”开关：开启后忽略白名单限制，可打开任意应用。</summary>
        public void SetOpenAppFullAccess(bool enabled)
        {
            OpenAppFullAccess = enabled;
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(ConfigFilePath));
                var root = document.RootElement;

                if (root.TryGetProperty("Theme", out var themeElement))
                {
                    var value = themeElement.GetString();
                    if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
                    {
                        Theme = true;
                    }
                    else if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
                    {
                        Theme = false;
                    }
                }

                if (root.TryGetProperty("DeveloperMode", out var developerElement) &&
                    developerElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    DeveloperMode = developerElement.GetBoolean();
                }

                if (root.TryGetProperty("DebugBones", out var debugBonesElement) &&
                    debugBonesElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    DebugBones = debugBonesElement.GetBoolean();
                }

                if (root.TryGetProperty("WindowLeft", out var windowLeftElement) &&
                    windowLeftElement.ValueKind == JsonValueKind.Number)
                {
                    WindowLeft = windowLeftElement.GetDouble();
                }

                if (root.TryGetProperty("WindowTop", out var windowTopElement) &&
                    windowTopElement.ValueKind == JsonValueKind.Number)
                {
                    WindowTop = windowTopElement.GetDouble();
                }

                if (root.TryGetProperty("ActiveCharacterId", out var activeCharacterElement) &&
                    activeCharacterElement.ValueKind == JsonValueKind.String)
                {
                    ActiveCharacterId = activeCharacterElement.GetString()?.Trim() ?? string.Empty;
                }

                if (root.TryGetProperty("LastSeenAnnouncementId", out var lastSeenAnnouncementElement) &&
                    lastSeenAnnouncementElement.ValueKind == JsonValueKind.String)
                {
                    LastSeenAnnouncementId = lastSeenAnnouncementElement.GetString()?.Trim() ?? string.Empty;
                }

                if (root.TryGetProperty("ModelSavingMode", out var savingModeElement) &&
                    savingModeElement.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<SavingMode>(savingModeElement.GetString(), ignoreCase: true, out var savingMode) &&
                    Enum.IsDefined(savingMode))
                {
                    ModelSavingMode = savingMode;
                }

                if (root.TryGetProperty("CustomTouchReactionUseAi", out var touchReactionElement) &&
                    touchReactionElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomTouchReactionUseAi = touchReactionElement.GetBoolean();
                }

                if (root.TryGetProperty("CustomUpdateNotificationUseAi", out var notificationElement) &&
                    notificationElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomUpdateNotificationUseAi = notificationElement.GetBoolean();
                }

                if (root.TryGetProperty("CustomSnapBackUseAi", out var snapBackElement) &&
                    snapBackElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomSnapBackUseAi = snapBackElement.GetBoolean();
                }

                if (root.TryGetProperty("ProactiveChatEnabled", out var proactiveEnabledElement) &&
                    proactiveEnabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ProactiveChatEnabled = proactiveEnabledElement.GetBoolean();
                }

                if (root.TryGetProperty("ProactiveChatFrequency", out var proactiveFrequencyElement) &&
                    proactiveFrequencyElement.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<ProactiveFrequency>(proactiveFrequencyElement.GetString(), ignoreCase: true, out var proactiveFrequency) &&
                    Enum.IsDefined(proactiveFrequency))
                {
                    ProactiveChatFrequency = proactiveFrequency;
                }

                if (root.TryGetProperty("QuietHoursEnabled", out var quietEnabledElement) &&
                    quietEnabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    QuietHoursEnabled = quietEnabledElement.GetBoolean();
                }

                if (root.TryGetProperty("QuietHoursStart", out var quietStartElement) &&
                    quietStartElement.ValueKind == JsonValueKind.Number)
                {
                    QuietHoursStart = Math.Clamp(quietStartElement.GetInt32(), 0, 23);
                }

                if (root.TryGetProperty("QuietHoursEnd", out var quietEndElement) &&
                    quietEndElement.ValueKind == JsonValueKind.Number)
                {
                    QuietHoursEnd = Math.Clamp(quietEndElement.GetInt32(), 0, 23);
                }

                if (root.TryGetProperty("ProactiveIdleEnabled", out var proactiveIdleElement) &&
                    proactiveIdleElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ProactiveIdleEnabled = proactiveIdleElement.GetBoolean();
                }

                if (root.TryGetProperty("ProactiveWelcomeBackEnabled", out var proactiveWelcomeElement) &&
                    proactiveWelcomeElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ProactiveWelcomeBackEnabled = proactiveWelcomeElement.GetBoolean();
                }

                if (root.TryGetProperty("ProactiveGreetingEnabled", out var proactiveGreetingElement) &&
                    proactiveGreetingElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ProactiveGreetingEnabled = proactiveGreetingElement.GetBoolean();
                }

                if (root.TryGetProperty("CustomProactiveChatUseAi", out var proactiveAiElement) &&
                    proactiveAiElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomProactiveChatUseAi = proactiveAiElement.GetBoolean();
                }

                if (root.TryGetProperty("CustomEmotionJudgeUseAi", out var emotionJudgeAiElement) &&
                    emotionJudgeAiElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomEmotionJudgeUseAi = emotionJudgeAiElement.GetBoolean();
                }

                if (root.TryGetProperty("CustomMemoryExtractionGated", out var memoryExtractionGatedElement) &&
                    memoryExtractionGatedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    CustomMemoryExtractionGated = memoryExtractionGatedElement.GetBoolean();
                }

                if (root.TryGetProperty("ForegroundAppContextEnabled", out var foregroundAppElement) &&
                    foregroundAppElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ForegroundAppContextEnabled = foregroundAppElement.GetBoolean();
                }

                if (root.TryGetProperty("OpenAppEnabled", out var openAppElement) &&
                    openAppElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    OpenAppEnabled = openAppElement.GetBoolean();
                }

                if (root.TryGetProperty("OpenAppFullAccess", out var openAppFullElement) &&
                    openAppFullElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    OpenAppFullAccess = openAppFullElement.GetBoolean();
                }

                if (root.TryGetProperty("AllowedApps", out var allowedAppsElement) &&
                    allowedAppsElement.ValueKind == JsonValueKind.Array)
                {
                    var apps = new List<AllowedAppEntry>();
                    foreach (var item in allowedAppsElement.EnumerateArray())
                    {
                        var appName = item.TryGetProperty("Name", out var nameElement) &&
                                      nameElement.ValueKind == JsonValueKind.String
                            ? nameElement.GetString()?.Trim() ?? string.Empty
                            : string.Empty;

                        var appPath = item.TryGetProperty("ExecutablePath", out var pathElement) &&
                                      pathElement.ValueKind == JsonValueKind.String
                            ? pathElement.GetString()?.Trim() ?? string.Empty
                            : string.Empty;

                        // 名称与路径都有效才算一条，避免残缺配置让工具拿到空值
                        if (appName.Length > 0 && appPath.Length > 0)
                        {
                            apps.Add(new AllowedAppEntry { Name = appName, ExecutablePath = appPath });
                        }
                    }

                    AllowedApps = apps;
                }
            }
            catch (Exception ex)
            {
                // 配置损坏时视为未设置，各项回退到默认值
                Log.Warning($"[设置] 读取失败，改用默认设置：{ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                var json = JsonSerializer.Serialize(new
                {
                    Theme = Theme switch { true => "Dark", false => "Light", null => "System" },
                    DeveloperMode,
                    DebugBones,
                    WindowLeft,
                    WindowTop,
                    ActiveCharacterId,
                    LastSeenAnnouncementId,
                    ModelSavingMode = ModelSavingMode.ToString(),
                    CustomTouchReactionUseAi,
                    CustomUpdateNotificationUseAi,
                    CustomSnapBackUseAi,
                    ProactiveChatEnabled,
                    ProactiveChatFrequency = ProactiveChatFrequency.ToString(),
                    QuietHoursEnabled,
                    QuietHoursStart,
                    QuietHoursEnd,
                    ProactiveIdleEnabled,
                    ProactiveWelcomeBackEnabled,
                    ProactiveGreetingEnabled,
                    CustomProactiveChatUseAi,
                    CustomEmotionJudgeUseAi,
                    CustomMemoryExtractionGated,
                    ForegroundAppContextEnabled,
                    OpenAppEnabled,
                    OpenAppFullAccess,
                    AllowedApps
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                // 保存失败不阻塞使用，下次启动回退到默认设置
                Log.Error($"[设置] 保存失败：{ex.Message}");
            }
        }
    }
}
