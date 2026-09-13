using System.Text.Json;
using KfuPet.Services;

namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 归档记忆的持久化存储：短期与长期之间的缓冲，以 JSON 形式保存。
    /// </summary>
    internal class ArchiveMemoryStore
    {
        private readonly string _directory;
        private readonly string _filePath;

        public ArchiveMemoryStore()
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Memory");
            _filePath = Path.Combine(_directory, "ArchiveMemory.json");
        }

        /// <summary>从磁盘加载归档记忆，文件不存在或损坏时返回空列表。</summary>
        public List<ArchiveEntry> Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<ArchiveEntry>();
                }

                var json = File.ReadAllText(_filePath);
                var entries = JsonSerializer.Deserialize<List<ArchiveEntry>>(json) ?? new List<ArchiveEntry>();
                Log.Debug($"[记忆] 归档记忆已加载：{entries.Count} 条");
                return entries;
            }
            catch (Exception ex)
            {
                Log.Warning($"[记忆] 归档记忆读取失败，按空列表处理：{ex.Message}");
                return new List<ArchiveEntry>();
            }
        }

        /// <summary>把归档记忆写回磁盘。</summary>
        public void Save(IReadOnlyList<ArchiveEntry> entries)
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
            catch (Exception ex)
            {
                // 写入失败不阻断对话
                Log.Error($"[记忆] 归档记忆写入失败：{ex.Message}");
            }
        }
    }
}
