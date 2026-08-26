using System.Text.Json;

namespace KfuPet.Services
{
    /// <summary>
    /// 管理关键词检索的停用词，加载 / 保存到 %AppData%\KfuPet\stopwords.json。
    /// 配置缺失或损坏时回退到内置默认停用词。
    /// </summary>
    internal class StopWordsService
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "stopwords.json");

        private List<string> _words;

        public StopWordsService()
        {
            _words = Load();
        }

        /// <summary>当前停用词列表（只读视图）。</summary>
        public IReadOnlyList<string> Words => _words;

        /// <summary>保存停用词：每项去空白、去空项、去重后写回 JSON。</summary>
        public void Save(IEnumerable<string> words)
        {
            _words = Normalize(words);

            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var json = JsonSerializer.Serialize(_words, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // 写入失败不影响界面操作
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
            catch
            {
                // 配置缺失、损坏或为空时使用默认停用词
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

        /// <summary>中文检索常见停用词，用于关键词提取时过滤虚词/疑问词。</summary>
        private static readonly List<string> DefaultWords = new()
        {
            "什么", "怎么", "怎样", "如何", "为什么", "哪个", "哪些", "多少", "是不是", "有没有",
            "你", "我", "他", "她", "它", "你们", "我们", "他们", "咱们",
            "的", "了", "吗", "呢", "啊", "吧", "哦", "呀", "啦",
            "这", "那", "这个", "那个", "这些", "那些", "一个", "一些",
            "是", "在", "有", "和", "与", "或者", "还是", "都", "就", "很", "不", "没", "也", "还",
            "可以", "能", "要", "会", "想", "知道", "觉得", "喜欢", "记得", "忘记",
            "告诉", "说", "请问", "帮我", "跟我", "聊聊", "一下"
        };
    }
}
