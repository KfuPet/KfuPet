using System.Text.Json;
using KfuPet.Services;

namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 短期记忆的持久化存储：以 JSON 形式保存最近会话的对话历史，
    /// 与归档、长期记忆分开存放，重启后仍可恢复会话上下文。
    /// </summary>
    internal class ShortTermMemoryStore
    {
        private string _directory;
        private string _filePath;

        /// <summary>在角色作用域目录下读写短期记忆。</summary>
        public ShortTermMemoryStore(string directory)
        {
            _directory = directory;
            _filePath = Path.Combine(_directory, CharacterScopeService.ShortMemoryFileName);
        }

        /// <summary>切换角色作用域：后续读写改用新目录（调用方需保证没有并发读写）。</summary>
        public void SetDirectory(string directory)
        {
            _directory = directory;
            _filePath = Path.Combine(_directory, CharacterScopeService.ShortMemoryFileName);
        }

        /// <summary>从磁盘加载短期记忆，文件不存在或损坏时返回空列表。</summary>
        public List<ShortMemoryEntry> Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<ShortMemoryEntry>();
                }

                var json = File.ReadAllText(_filePath);
                var entries = JsonSerializer.Deserialize<List<ShortMemoryEntry>>(json) ?? new List<ShortMemoryEntry>();
                Log.Debug($"[记忆] 短期记忆已加载：{entries.Count} 条");
                return entries;
            }
            catch (Exception ex)
            {
                Log.Warning($"[记忆] 短期记忆读取失败，按空列表处理：{ex.Message}");
                return new List<ShortMemoryEntry>();
            }
        }

        /// <summary>把短期记忆写回磁盘。</summary>
        public void Save(IReadOnlyList<ShortMemoryEntry> entries)
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
                Log.Error($"[记忆] 短期记忆写入失败：{ex.Message}");
            }
        }
    }
}
