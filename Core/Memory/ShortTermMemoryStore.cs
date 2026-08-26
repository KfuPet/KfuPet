using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Core.Memory
{
    /// <summary>
    /// 短期记忆（会话上下文）的持久化存储：以 JSON 形式保存最近会话的对话历史，
    /// 与长期记忆（memories.md + vectors.json）分开存放，重启后仍可恢复会话上下文。
    /// </summary>
    internal class ShortTermMemoryStore
    {
        private readonly string _directory;
        private readonly string _filePath;

        public ShortTermMemoryStore()
        {
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet", "Memory");
            _filePath = Path.Combine(_directory, "short-term.json");
        }

        /// <summary>从磁盘加载短期记忆，文件不存在或损坏时返回空列表。</summary>
        public List<ChatMessage> Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<ChatMessage>();
                }

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            }
            catch
            {
                return new List<ChatMessage>();
            }
        }

        /// <summary>把短期记忆写回磁盘。</summary>
        public void Save(IReadOnlyList<ChatMessage> messages)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var json = JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // 写入失败不阻断对话
            }
        }
    }
}
