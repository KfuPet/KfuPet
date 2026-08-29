using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>
    /// 管理外观主题偏好，加载 / 保存到本地 JSON 文件（%AppData%\KfuPet\settings.json）。
    /// 未保存过偏好时跟随系统主题。
    /// </summary>
    internal class ThemeService
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");

        /// <summary>主题偏好：true 深色，false 浅色，null 跟随系统。</summary>
        public bool? PreferredDark { get; private set; }

        public ThemeService()
        {
            Load();
        }

        /// <summary>保存用户的外观选择：true 深色，false 浅色，null 跟随系统。</summary>
        public void SavePreference(bool? isDark)
        {
            PreferredDark = isDark;
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
                if (!document.RootElement.TryGetProperty("Theme", out var themeElement))
                {
                    return;
                }

                var value = themeElement.GetString();
                if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    PreferredDark = true;
                }
                else if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
                {
                    PreferredDark = false;
                }
            }
            catch
            {
                // 配置损坏时视为未设置，跟随系统主题
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var json = JsonSerializer.Serialize(new { Theme = PreferredDark switch { true => "Dark", false => "Light", null => "System" } });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // 保存失败不阻塞使用，下次启动会回退到跟随系统
            }
        }
    }
}
