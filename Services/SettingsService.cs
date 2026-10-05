using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>模型功能节省模式：控制触摸反应、更新通知文案与越界回正文案是否改用内置文案。</summary>
    internal enum SavingMode
    {
        /// <summary>关闭：不启用节省，各功能保持现状（由 AI 生成）。</summary>
        Off,

        /// <summary>节省：各功能改用内置文案，不再消耗模型请求。</summary>
        Saving,

        /// <summary>自定义：各功能分别控制是否使用 AI 生成。</summary>
        Custom
    }

    /// <summary>
    /// 应用通用设置，统一读写 %AppData%\KfuPet\Config\settings.json。
    /// 目前包含主题偏好、开发者模式、骨骼调试线框、桌宠窗口位置与模型功能节省模式；后续通用设置继续在此扩展字段。
    /// </summary>
    internal class SettingsService
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Config");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");

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

        /// <summary>模型功能节省模式。</summary>
        public SavingMode ModelSavingMode { get; private set; }

        /// <summary>自定义模式下触摸反应是否使用 AI 生成；false 表示改用内置文案。</summary>
        public bool CustomTouchReactionUseAi { get; private set; } = true;

        /// <summary>自定义模式下更新通知文案是否使用 AI 生成；false 表示改用内置文案。</summary>
        public bool CustomUpdateNotificationUseAi { get; private set; } = true;

        /// <summary>自定义模式下越界回正文案是否使用 AI 生成；false 表示改用内置文案。</summary>
        public bool CustomSnapBackUseAi { get; private set; } = true;

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

        private SettingsService()
        {
            Load();
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
                Directory.CreateDirectory(ConfigDirectory);
                var json = JsonSerializer.Serialize(new
                {
                    Theme = Theme switch { true => "Dark", false => "Light", null => "System" },
                    DeveloperMode,
                    DebugBones,
                    WindowLeft,
                    WindowTop,
                    ModelSavingMode = ModelSavingMode.ToString(),
                    CustomTouchReactionUseAi,
                    CustomUpdateNotificationUseAi,
                    CustomSnapBackUseAi
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
