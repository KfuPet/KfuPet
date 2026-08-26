using System.Text.Json;

namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 长期记忆的持久化存储：记忆正文与元数据统一写入 <c>LongMemory.json</c>（单一 JSON 文件）。
    /// 记忆统一存放在 %AppData%\KfuPet\Memory。
    /// </summary>
    internal class MemoryStore
    {
        private readonly string _directory;
        private readonly string _filePath;

        public MemoryStore()
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Memory");
            _filePath = Path.Combine(_directory, "LongMemory.json");
        }

        /// <summary>从磁盘加载全部长期记忆，文件不存在或损坏时返回空列表。</summary>
        public List<MemoryEntry> Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<MemoryEntry>();
                }

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<MemoryEntry>>(json) ?? new List<MemoryEntry>();
            }
            catch
            {
                return new List<MemoryEntry>();
            }
        }

        /// <summary>把全部长期记忆写回磁盘。</summary>
        public void Save(IReadOnlyList<MemoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // 写入失败不阻断对话主流程
            }
        }
    }
}
