using System.Text.Json;
using KfuPet.Core.Memory;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 记忆系统门面：统一管理短期 → 归档 → 长期三级记忆的流转。
    /// 短期记忆保存最近对话，满 20 条溢出到归档；归档满 40 条后后台异步触发
    /// AI 分析筛选，重要信息进长期记忆，其余总结后留在归档继续循环。
    /// </summary>
    internal class MemorySystem
    {
        private const int ShortMemoryLimit = 20;
        private const int ArchiveMemoryLimit = 40;
        private const int LongMemoryLimit = 500;

        private readonly ShortTermMemoryStore _shortStore = new();
        private readonly ArchiveMemoryStore _archiveStore = new();
        private readonly MemoryManager _memoryManager;
        private readonly PromptService _promptService = new();
        private readonly ChatService _chatService;
        private readonly LogService _logService;

        private readonly List<ShortMemoryEntry> _shortEntries;
        private readonly List<ArchiveEntry> _archiveEntries;
        private readonly object _archiveLock = new();
        private volatile bool _isAnalyzing;

        public MemorySystem(ChatService chatService, LogService logService, StopWordsService stopWordsService)
        {
            _chatService = chatService;
            _logService = logService;
            _memoryManager = new MemoryManager(stopWordsService);
            _shortEntries = _shortStore.Load();
            _archiveEntries = _archiveStore.Load();
        }

        /// <summary>短期记忆当前条数（上限 <see cref="ShortMemoryLimit"/>）。</summary>
        public int ShortCount
        {
            get
            {
                lock (_archiveLock)
                {
                    return _shortEntries.Count;
                }
            }
        }

        /// <summary>归档记忆当前条数（达到 <see cref="ArchiveMemoryLimit"/> 触发分析）。</summary>
        public int ArchiveCount
        {
            get
            {
                lock (_archiveLock)
                {
                    return _archiveEntries.Count;
                }
            }
        }

        /// <summary>长期记忆当前条数（上限 <see cref="LongMemoryLimit"/>）。</summary>
        public int LongCount => _memoryManager.Count;

        /// <summary>短期记忆容量上限。</summary>
        public static int ShortCapacity => ShortMemoryLimit;

        /// <summary>归档记忆容量上限。</summary>
        public static int ArchiveCapacity => ArchiveMemoryLimit;

        /// <summary>长期记忆容量上限。</summary>
        public static int LongCapacity => LongMemoryLimit;

        /// <summary>
        /// 获取聊天记录快照：归档中的原始对话 + 短期记忆，按时间升序排列，供设置页展示。
        /// </summary>
        public IReadOnlyList<ShortMemoryEntry> GetChatHistory()
        {
            lock (_archiveLock)
            {
                var result = new List<ShortMemoryEntry>();
                foreach (var entry in _archiveEntries)
                {
                    // 总结条目不是对话，不放进聊天记录
                    if (!entry.IsSummary)
                    {
                        result.Add(new ShortMemoryEntry
                        {
                            Time = entry.Time,
                            User = entry.User,
                            Assistant = entry.Assistant
                        });
                    }
                }
                result.AddRange(_shortEntries);
                return result.OrderBy(e => e.Time).ToList();
            }
        }

        /// <summary>获取全部长期记忆快照（重要性高的在前），供设置页展示。</summary>
        public IReadOnlyList<MemoryEntry> GetLongTermMemories()
        {
            return _memoryManager.Snapshot()
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.CreatedAt)
                .ToList();
        }

        /// <summary>把短期记忆转成对话消息列表，供 AI 作为会话上下文使用。</summary>
        public IReadOnlyList<ChatMessage> GetShortTermMessages()
        {
            var messages = new List<ChatMessage>();
            foreach (var entry in _shortEntries)
            {
                messages.Add(new ChatMessage { Role = "user", Content = entry.User });
                messages.Add(new ChatMessage { Role = "assistant", Content = entry.Assistant });
            }
            return messages;
        }

        /// <summary>构建系统提示词（全局 + 角色）并注入检索到的长期记忆。</summary>
        public async Task<string> BuildContextAsync(ModelConfig model, string userMessage)
        {
            var systemPrompt = _promptService.BuildSystemPrompt();

            var memories = await RetrieveMemoriesAsync(model, userMessage);
            if (memories.Count > 0)
            {
                systemPrompt += "\n\n关于主人的记忆（你已知晓）：\n" + MemoryManager.BuildContext(memories);
            }
            else
            {
                // 长期记忆未命中时，兜底检索归档记忆，避免归档内容白白浪费
                var archiveHits = await SearchArchiveAsync(model, userMessage);
                _logService.Debug($"[记忆] 长期记忆未命中，归档兜底检索到 {archiveHits.Count} 条相关记录");
                if (archiveHits.Count > 0)
                {
                    systemPrompt += "\n\n最近对话中提到的相关信息（供参考）：\n" + MemoryManager.BuildContext(archiveHits);
                }
            }

            return systemPrompt;
        }

        /// <summary>
        /// 检索长期记忆，按优先级降级：关键词匹配 → 模型判断相关性。
        /// </summary>
        private async Task<IReadOnlyList<string>> RetrieveMemoriesAsync(ModelConfig model, string userMessage)
        {
            // 1. 关键词匹配（本地，零成本）
            var keywordHits = _memoryManager.KeywordSearch(userMessage, 5);
            if (keywordHits.Count > 0)
            {
                _logService.Info($"[记忆] 关键词匹配命中 {keywordHits.Count} 条相关记忆");
                return keywordHits;
            }

            // 2. 模型判断相关性（兜底，需要一次 API 调用）
            var llmHits = await SearchByLlmAsync(model, userMessage, 5);
            _logService.Info($"[记忆] 关键词未命中，改用模型判断相关性，命中 {llmHits.Count} 条");
            return llmHits;
        }

        /// <summary>
        /// 模型判断相关性：把长期记忆交给 AI，让它挑出与当前话题相关的记忆（关键词检索的兜底方案）。
        /// </summary>
        private async Task<IReadOnlyList<string>> SearchByLlmAsync(ModelConfig model, string userMessage, int topK)
        {
            var snapshot = _memoryManager.Snapshot();
            if (snapshot.Count == 0)
            {
                return Array.Empty<string>();
            }

            const string prompt = """
                下面是已记住的关于用户的信息。请判断哪些信息与用户当前话题相关，
                把相关的信息原文摘出来（保持原样，不要改写）。
                请只输出一个 JSON 字符串数组，不要包含其他任何文字或代码块标记，格式：["信息1", "信息2"]
                没有相关的信息时输出空数组 []。
                """;

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < snapshot.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {snapshot[i].Content}");
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = $"当前用户话题：{userMessage}\n\n记忆列表：\n{sb}" }
            };

            var reply = await _chatService.SendRawAsync(model, messages);
            return ParseStringArray(reply).Take(topK).ToList();
        }

        /// <summary>
        /// 归档兜底检索：长期记忆未命中时，从归档的原始对话/总结中找与当前话题相关的记录。
        /// </summary>
        private async Task<List<string>> SearchArchiveAsync(ModelConfig model, string userMessage)
        {
            List<ArchiveEntry> snapshot;
            lock (_archiveLock)
            {
                snapshot = new List<ArchiveEntry>(_archiveEntries);
            }

            if (snapshot.Count == 0)
            {
                return new List<string>();
            }

            const string prompt = """
                下面是用户最近的一些对话记录。请判断哪些记录与用户当前话题相关，
                把相关的记录原文摘出来（可适度精简）。如果都不相关，输出空数组。
                请只输出一个 JSON 字符串数组，不要包含其他任何文字或代码块标记，格式：["记录1", "记录2"]
                """;

            var sb = new System.Text.StringBuilder();
            var index = 1;
            foreach (var entry in snapshot)
            {
                if (entry.IsSummary)
                {
                    sb.AppendLine($"{index}. {entry.Content}");
                }
                else
                {
                    sb.AppendLine($"{index}. 用户：{entry.User}；助手：{entry.Assistant}");
                }
                index++;
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = $"当前用户消息：{userMessage}\n\n归档记录：\n{sb}" }
            };

            var reply = await _chatService.SendRawAsync(model, messages);
            return ParseStringArray(reply);
        }

        /// <summary>
        /// 记录一轮对话：写入短期记忆，溢出到归档，并在归档满时后台触发分析。
        /// </summary>
        public void AddTurn(ModelConfig model, string user, string assistant)
        {
            var overflowCount = 0;
            var shortCount = 0;
            var archiveCount = 0;
            lock (_archiveLock)
            {
                _shortEntries.Add(new ShortMemoryEntry { User = user, Assistant = assistant });

                // 短期溢出到归档（保留最近 20 条）
                while (_shortEntries.Count > ShortMemoryLimit)
                {
                    var oldest = _shortEntries[0];
                    _shortEntries.RemoveAt(0);
                    _archiveEntries.Add(new ArchiveEntry
                    {
                        Type = "conversation",
                        Time = oldest.Time,
                        User = oldest.User,
                        Assistant = oldest.Assistant
                    });
                    overflowCount++;
                }

                shortCount = _shortEntries.Count;
                archiveCount = _archiveEntries.Count;
                _shortStore.Save(_shortEntries);
                _archiveStore.Save(_archiveEntries);
            }

            _logService.Debug($"[记忆] 记录一轮对话：短期 {shortCount}/{ShortMemoryLimit}" +
                              (overflowCount > 0 ? $"，溢出 {overflowCount} 条到归档（归档 {archiveCount}/{ArchiveMemoryLimit}）" : string.Empty));

            // 实时通道：核心信息（生日、姓名、重要偏好等）立即入库，不等归档批处理
            _ = Task.Run(() => RunRealtimeExtractAsync(model, user, assistant));

            // 归档满 40 条，后台异步分析
            if (_archiveEntries.Count >= ArchiveMemoryLimit && !_isAnalyzing)
            {
                _isAnalyzing = true;
                _ = Task.Run(() => RunArchiveAnalysisAsync(model));
            }
        }

        /// <summary>归档分析的外层包装：吞掉异常并复位状态位。</summary>
        private async Task RunArchiveAnalysisAsync(ModelConfig model)
        {
            try
            {
                await AnalyzeArchiveAsync(model);
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 归档分析失败：{ex.Message}");
            }
            finally
            {
                _isAnalyzing = false;
            }
        }

        /// <summary>实时提取的外层包装：吞掉异常，不影响主流程。</summary>
        private async Task RunRealtimeExtractAsync(ModelConfig model, string user, string assistant)
        {
            try
            {
                _logService.Debug("[记忆] 实时通道：开始提取核心信息...");
                var items = await ExtractCoreInfoAsync(model, user, assistant);
                var coreItems = items.Where(i => i.Importance >= 4).ToList();
                _logService.Debug(coreItems.Count > 0
                    ? $"[记忆] 实时通道：提取到 {coreItems.Count} 条核心信息"
                    : "[记忆] 实时通道：未发现需要立即记住的核心信息");
                foreach (var item in coreItems)
                {
                    await _memoryManager.StoreAsync(item.Content, item.Importance);
                    _logService.Info($"[记忆] 实时入库核心信息：{item.Content}（重要性 {item.Importance}）");
                }
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 实时提取失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 实时通道：判断本轮对话是否包含需要立即记住的核心信息（生日、姓名、重要偏好等），
        /// 仅提取重要性 ≥ 4 的信息，绕过归档批处理的等待。
        /// </summary>
        private async Task<List<MemoryItem>> ExtractCoreInfoAsync(ModelConfig model, string user, string assistant)
        {
            const string prompt = """
                你是记忆提取器。判断下面这轮对话中是否包含关于用户的、需要立即记住的核心信息，
                例如：生日、姓名、性别、年龄、住址、联系方式、重要偏好、长期目标、重要关系、重要事件等。

                只提取重要性 ≥ 4 的核心信息（4 用户偏好/长期目标，5 核心用户信息），
                忽略普通聊天、临时信息、寒暄和琐碎内容。

                请只输出一个 JSON 数组，不要包含其他任何文字或代码块标记，格式如下：
                [{"content": "一句话概括的信息", "importance": 4}]
                没有需要立即记住的核心信息时输出空数组 []。
                """;

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = $"用户说：{user}\n助手回复：{assistant}" }
            };

            var reply = await _chatService.SendRawAsync(model, messages);
            return ParseItems(reply);
        }

        /// <summary>
        /// 归档分析：对归档内容提炼信息点并评分，重要者进长期，其余总结留归档。
        /// </summary>
        private async Task AnalyzeArchiveAsync(ModelConfig model)
        {
            List<ArchiveEntry> snapshot;
            lock (_archiveLock)
            {
                snapshot = new List<ArchiveEntry>(_archiveEntries);
            }

            if (snapshot.Count == 0)
            {
                return;
            }

            _logService.Debug($"[记忆] 归档已满（{snapshot.Count} 条），开始分析筛选...");

            var summaryEntries = snapshot.Where(e => e.IsSummary).ToList();
            var conversationEntries = snapshot.Where(e => !e.IsSummary).ToList();

            var longTermItems = new List<MemoryItem>();
            var lowItems = new List<string>();

            // 1. 总结条目：重新评分，≥3 进长期，<3 丢弃
            var summaryPromoted = 0;
            foreach (var entry in summaryEntries)
            {
                var importance = await ScoreContentAsync(model, entry.Content);
                if (importance >= 3)
                {
                    longTermItems.Add(new MemoryItem(entry.Content, importance));
                    summaryPromoted++;
                }
            }
            if (summaryEntries.Count > 0)
            {
                _logService.Debug($"[记忆] 总结条目重新评分：{summaryPromoted}/{summaryEntries.Count} 条入选长期");
            }

            // 2. 原始对话条目：提炼信息点并评分
            if (conversationEntries.Count > 0)
            {
                var extracted = await ExtractFromConversationsAsync(model, conversationEntries);
                _logService.Debug($"[记忆] 从 {conversationEntries.Count} 条原始对话提炼出 {extracted.Count} 个信息点");
                foreach (var item in extracted)
                {
                    if (item.Importance >= 3)
                    {
                        longTermItems.Add(item);
                    }
                    else
                    {
                        lowItems.Add(item.Content);
                    }
                }
            }

            // 3. 入选信息写入长期记忆
            foreach (var item in longTermItems)
            {
                await _memoryManager.StoreAsync(item.Content, item.Importance);
                _logService.Info($"[记忆] 已写入长期记忆：{item.Content}（重要性 {item.Importance}）");
            }

            // 4. 未入选信息：总结成一条 / 暂留 / 清空
            ArchiveEntry? remaining = null;
            if (lowItems.Count >= 2)
            {
                var summary = await SummarizeAsync(model, lowItems);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    remaining = new ArchiveEntry
                    {
                        Type = "summary",
                        Content = summary,
                        IsSummary = true
                    };
                    _logService.Debug($"[记忆] {lowItems.Count} 条未入选信息总结成一条留归档");
                }
            }
            else if (lowItems.Count == 1)
            {
                remaining = new ArchiveEntry
                {
                    Type = "summary",
                    Content = lowItems[0],
                    IsSummary = false
                };
                _logService.Debug("[记忆] 1 条未入选信息暂留归档");
            }

            // 5. 归档重建
            lock (_archiveLock)
            {
                _archiveEntries.Clear();
                if (remaining != null)
                {
                    _archiveEntries.Add(remaining);
                }
                _archiveStore.Save(_archiveEntries);
            }

            _logService.Debug($"[记忆] 归档分析完成：入选 {longTermItems.Count} 条，总结/暂留 {lowItems.Count} 条");

            // 6. 长期记忆满 500 时去重 + 压缩
            if (_memoryManager.Count >= LongMemoryLimit)
            {
                await HandleLongMemoryOverflowAsync(model);
            }
        }

        /// <summary>长期记忆超限处理：先去重，再对最旧 100 条总结成一条。</summary>
        private async Task HandleLongMemoryOverflowAsync(ModelConfig model)
        {
            _logService.Debug("[记忆] 长期记忆达到上限，开始去重...");
            _memoryManager.Deduplicate();

            if (_memoryManager.Count < LongMemoryLimit)
            {
                return;
            }

            var snapshot = _memoryManager.Snapshot();
            var oldest = snapshot.OrderBy(e => e.CreatedAt).Take(100).ToList();
            if (oldest.Count == 0)
            {
                return;
            }

            var summary = await SummarizeAsync(model, oldest.Select(e => e.Content).ToList());
            _memoryManager.RemoveByIds(oldest.Select(e => e.Id).ToList());
            if (!string.IsNullOrWhiteSpace(summary))
            {
                await _memoryManager.StoreAsync(summary, 4);
            }

            _logService.Info($"[记忆] 长期记忆整理：去重并总结最旧 {oldest.Count} 条");
        }

        /// <summary>从原始对话中提炼值得长期记住的信息点并评分。</summary>
        private async Task<List<MemoryItem>> ExtractFromConversationsAsync(
            ModelConfig model, IReadOnlyList<ArchiveEntry> entries)
        {
            const string prompt = """
                你是记忆提取器。从以下对话中提取值得长期记住的、关于用户的信息
                （如名字、喜好、习惯、经历、目标、重要关系等）。
                只提取稳定且长期有用的信息，忽略临时、琐碎或纯寒暄的内容。

                对每条提取的信息进行重要性评分（0~5 整数）：
                0 临时信息，1 普通聊天，2 可能有用的信息，3 用户习惯，4 用户偏好/长期目标，5 核心用户信息。

                请只输出一个 JSON 数组，不要包含其他任何文字或代码块标记，格式如下：
                [{"content": "一句话概括的信息", "importance": 4}]
                没有值得记住的信息时输出空数组 []。
                """;

            var sb = new System.Text.StringBuilder();
            foreach (var entry in entries)
            {
                sb.AppendLine($"用户说：{entry.User}");
                sb.AppendLine($"助手回复：{entry.Assistant}");
                sb.AppendLine();
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = sb.ToString() }
            };

            var reply = await _chatService.SendRawAsync(model, messages);
            return ParseItems(reply);
        }

        /// <summary>对单条信息重新评分（用于总结条目丢弃前的评估）。</summary>
        private async Task<int> ScoreContentAsync(ModelConfig model, string content)
        {
            const string prompt = """
                对下面这条信息进行重要性评分（0~5 整数）：
                0 临时信息，1 普通聊天，2 可能有用的信息，3 用户习惯，4 用户偏好/长期目标，5 核心用户信息。

                请只输出一个 JSON 对象，不要包含其他任何文字，格式：{"importance": 4}
                """;

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = $"信息：{content}" }
            };

            var reply = await _chatService.SendRawAsync(model, messages);
            return ParseScore(reply);
        }

        /// <summary>把多条零散信息总结成一句话。</summary>
        private async Task<string> SummarizeAsync(ModelConfig model, IReadOnlyList<string> contents)
        {
            const string prompt = """
                把下面多条零散信息总结成一句话，保留关键事实，不要遗漏重要点。
                请只输出总结后的一句话，不要包含其他任何文字。
                """;

            var joined = string.Join("\n", contents.Select(c => "- " + c));

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = joined }
            };

            return (await _chatService.SendRawAsync(model, messages)).Trim();
        }

        /// <summary>解析信息点数组 JSON（失败时返回空列表）。</summary>
        private static List<MemoryItem> ParseItems(string reply)
        {
            var text = StripCodeFence(reply);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var result = new List<MemoryItem>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var content = item.TryGetProperty("content", out var c) ? c.GetString()?.Trim() ?? string.Empty : string.Empty;
                    var importance = item.TryGetProperty("importance", out var i) && i.TryGetInt32(out var v) ? v : 0;
                    if (!string.IsNullOrEmpty(content))
                    {
                        result.Add(new MemoryItem(content, Math.Clamp(importance, 0, 5)));
                    }
                }
                return result;
            }
            catch (JsonException)
            {
                return new List<MemoryItem>();
            }
        }

        /// <summary>解析评分 JSON，失败时返回 0。</summary>
        private static int ParseScore(string reply)
        {
            var text = StripCodeFence(reply);
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("importance", out var i) && i.TryGetInt32(out var v))
                {
                    return Math.Clamp(v, 0, 5);
                }
            }
            catch (JsonException)
            {
                // 解析失败返回 0
            }

            return 0;
        }

        /// <summary>解析 JSON 字符串数组（失败时返回空列表）。</summary>
        private static List<string> ParseStringArray(string reply)
        {
            var text = StripCodeFence(reply);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var result = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var s = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s))
                    {
                        result.Add(s);
                    }
                }
                return result;
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        /// <summary>剥离可能的 ```json ... ``` 代码块包裹。</summary>
        private static string StripCodeFence(string text)
        {
            var t = text.Trim();
            if (!t.StartsWith("```", StringComparison.Ordinal))
            {
                return t;
            }

            var newline = t.IndexOf('\n');
            if (newline >= 0)
            {
                t = t.Substring(newline + 1).Trim();
            }
            var fence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                t = t.Substring(0, fence).Trim();
            }
            return t;
        }

        /// <summary>一条提炼出的信息点：内容 + 重要性评分。</summary>
        private sealed record MemoryItem(string Content, int Importance);
    }
}
