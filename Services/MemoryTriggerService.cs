using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>管理“记忆触发词”：判断一条用户消息是否值得立即做核心信息提取——命中任一触发词才发起实时提取请求，避免每轮都多消耗一次模型。
    /// 加载/保存到缓存目录下的 Config\memory-triggers.json（默认 %AppData%\KfuPet），配置缺失或损坏时回退到内置默认触发词。</summary>
    internal class MemoryTriggerService
    {
        private static readonly string ConfigFilePath = Path.Combine(AppPaths.ConfigDirectory, "memory-triggers.json");

        private List<string> _words;

        public MemoryTriggerService()
        {
            _words = Load();
        }

        /// <summary>
        /// 恢复初始状态：改用内置默认触发词。
        /// 磁盘上的配置文件由“缓存清理”删除，之后保存会重新生成。
        /// </summary>
        public void ResetToDefaults()
        {
            _words = new List<string>(DefaultWords);
            Log.Info("[配置] 记忆触发词已恢复内置默认");
        }

        /// <summary>当前触发词列表（只读视图）。</summary>
        public IReadOnlyList<string> Words => _words;

        /// <summary>文本是否命中任一触发词（忽略大小写）；用于决定是否发起实时核心信息提取。</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text) || _words.Count == 0)
            {
                return false;
            }

            return _words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>保存触发词：每项去空白、去空项、去重后写回 JSON。</summary>
        public void Save(IEnumerable<string> words)
        {
            _words = Normalize(words);

            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                var json = JsonSerializer.Serialize(_words, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(ConfigFilePath, json);
                Log.Info($"[配置] 记忆触发词已保存：{_words.Count} 条");
            }
            catch (Exception ex)
            {
                // 写入失败不影响界面操作
                Log.Error($"[配置] 记忆触发词保存失败：{ex.Message}");
            }
        }

        private static List<string> Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var words = JsonSerializer.Deserialize<List<string>>(json);
                    if (words != null)
                    {
                        var normalized = Normalize(words);
                        if (normalized.Count > 0)
                        {
                            return normalized;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 配置缺失、损坏或为空时使用默认触发词
                Log.Warning($"[配置] 记忆触发词读取失败，改用内置默认触发词：{ex.Message}");
            }

            return new List<string>(DefaultWords);
        }

        private static List<string> Normalize(IEnumerable<string> words)
        {
            return words
                .Select(w => w.Trim())
                .Where(w => w.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>内置默认触发词：出现这些词时，说明消息可能在陈述值得立即记住的个人信息。</summary>
        private static readonly List<string> DefaultWords = new()
        {
            "我叫", "名字", "姓名", "性别", "生日", "年龄", "几岁", "住址", "住在", "电话", "号码", "邮箱",
            "喜欢", "讨厌", "最爱", "爱好", "擅长", "目标", "梦想",
            "家人", "爸爸", "妈妈", "朋友", "工作", "职业", "学校", "专业", "养了", "宠物"
        };
    }
}
