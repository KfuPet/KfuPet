using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>
    /// 应用通用设置，统一读写 %AppData%\KfuPet\Config\settings.json。
    /// 目前包含主题偏好、开发者模式、骨骼调试线框三项；后续通用设置继续在此扩展字段。
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
                    DebugBones
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
