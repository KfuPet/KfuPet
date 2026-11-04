using System.Text.Json;
using KfuPet.Core.Memory;
using KfuPet.Helpers;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>记忆系统门面：统一管理短期 → 归档 → 长期三级记忆的流转。
    /// 短期记忆保存最近对话，满 20 条溢出到归档；归档满 40 条后后台异步触发 AI 分析筛选，
    /// 重要信息进长期记忆，其余总结后留在归档继续循环。</summary>
    internal class MemorySystem
    {
        private const int ShortMemoryLimit = 20;
        private const int ArchiveMemoryLimit = 40;
        private const int LongMemoryLimit = 500;

        /// <summary>灰区候选的最低二元组相似度：低于该值视为全新信息，不交给模型判断。</summary>
        private const double DedupCandidateMinSimilarity = 0.35;

        /// <summary>每条疑似重复信息最多提供给模型的候选记忆条数。</summary>
        private const int DedupCandidateMaxCount = 3;

        /// <summary>单次去重判定请求最多包含的新信息条数，避免本地模型上下文过长。</summary>
        private const int DedupJudgeBatchSize = 8;

        /// <summary>每轮固定注入的重要性最高的长期记忆条数（核心事实不依赖关键词命中也能带上）。</summary>
        private const int AlwaysInjectMemoryCount = 5;

        /// <summary>关键词命中时补充注入的相关记忆条数上限。</summary>
        private const int KeywordMemoryCount = 5;

        /// <summary>关键词未命中时，模型相关性判断的最长等待时间；超时即放弃本次注入，不拖慢回复。</summary>
        private static readonly TimeSpan MemoryJudgeTimeout = TimeSpan.FromSeconds(3);

        private readonly ShortTermMemoryStore _shortStore = new();
        private readonly ArchiveMemoryStore _archiveStore = new();
        private readonly MemoryManager _memoryManager;
        private readonly PromptService _promptService = new();
        private readonly ChatService _chatService;
        private readonly LogService _logService;
        private readonly MemoryTriggerService _triggerService;

        private readonly List<ShortMemoryEntry> _shortEntries;
        private readonly List<ArchiveEntry> _archiveEntries;
        private readonly object _archiveLock = new();

        /// <summary>仍在实时提炼中的短期条目：归档分析遇到这些对话时跳过并保留，避免同一轮被两个通道同时提炼。</summary>
        private readonly HashSet<ShortMemoryEntry> _pendingRealtimeTurns = new();

        /// <summary>归档分析的并发闸门：0 = 空闲，1 = 分析进行中，避免多个触发同时启动分析。</summary>
        private int _archiveAnalysisRunning;

        /// <summary>长期记忆压缩的并发闸门：0 = 空闲，1 = 压缩进行中，避免实时通道与归档分析同时触发。</summary>
        private int _longCompactionRunning;

        public MemorySystem(ChatService chatService, LogService logService, StopWordsService stopWordsService, MemoryTriggerService triggerService)
        {
            _chatService = chatService;
            _logService = logService;
            _triggerService = triggerService;
            _memoryManager = new MemoryManager(stopWordsService);
            _shortEntries = _shortStore.Load();
            _archiveEntries = _archiveStore.Load();
        }

        /// <summary>从磁盘重新加载三级记忆，覆盖当前内存中的缓存。</summary>
        public void Reload()
        {
            _memoryManager.Reload();

            lock (_archiveLock)
            {
                _shortEntries.Clear();
                _shortEntries.AddRange(_shortStore.Load());

                _archiveEntries.Clear();
                _archiveEntries.AddRange(_archiveStore.Load());
            }
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

        /// <summary>本地关键词检索长期记忆（零模型开销），供 memory_search 工具主动回忆使用。</summary>
        /// <param name="query">检索关键词。</param>
        /// <param name="topK">最多返回条数。</param>
        public IReadOnlyList<string> SearchLongTerm(string query, int topK)
        {
            var hits = _memoryManager.KeywordSearch(query, topK);
            _logService.Info($"[记忆] 工具检索长期记忆「{query}」，命中 {hits.Count} 条");
            return hits;
        }

        /// <summary>清空短期记忆并落盘。</summary>
        public void ClearShortTerm()
        {
            lock (_archiveLock)
            {
                _shortEntries.Clear();
                _shortStore.Save(_shortEntries);
            }
        }

        /// <summary>清空归档记忆并落盘。</summary>
        public void ClearArchive()
        {
            lock (_archiveLock)
            {
                _archiveEntries.Clear();
                _archiveStore.Save(_archiveEntries);
            }
        }

        /// <summary>清空全部长期记忆并落盘。</summary>
        public void ClearLongTerm()
        {
            _memoryManager.Clear();
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

        /// <summary>
        /// 构建基础系统提示词（全局 + 角色 + 当前时间 + 当前情绪，不含记忆检索）。
        /// 供触摸反应等轻量请求复用，避免为一句短回应触发记忆检索。
        /// </summary>
        public string BuildBaseSystemPrompt()
        {
            return _promptService.BuildSystemPrompt() + EmotionService.Instance.BuildPromptContext();
        }

        /// <summary>构建系统提示词（全局 + 角色 + 当前情绪）并注入长期记忆。
        /// 先固定注入重要性最高的若干条（核心事实始终在场），再叠加关键词命中的相关记忆；
        /// 关键词完全未命中时才用模型判断相关性兜底，且带超时保护，避免拖慢回复。</summary>
        public async Task<string> BuildContextAsync(ModelConfig model, string userMessage)
        {
            var systemPrompt = BuildBaseSystemPrompt();

            // 1. 固定注入：重要性最高的若干条，姓名 / 生日 / 关键偏好等核心事实不依赖关键词命中
            var important = _memoryManager.Snapshot()
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.CreatedAt)
                .Take(AlwaysInjectMemoryCount)
                .Select(e => e.Content)
                .ToList();

            // 2. 关键词命中：补充与当前话题相关的记忆（本地检索，零模型开销）
            var keywordHits = _memoryManager.KeywordSearch(userMessage, KeywordMemoryCount);
            if (keywordHits.Count > 0)
            {
                _logService.Info($"[记忆] 关键词匹配命中 {keywordHits.Count} 条相关记忆");
            }
            else
            {
                // 3. 关键词未命中时才走模型判断兜底，带超时：超时或失败即放弃，不阻塞回复
                keywordHits = await SearchByLlmWithTimeoutAsync(model, userMessage, KeywordMemoryCount);
            }

            var memories = MergeMemories(important, keywordHits);
            if (memories.Count > 0)
            {
                systemPrompt += "\n\n关于主人的记忆（你已知晓）：\n" + MemoryManager.BuildContext(memories);
            }

            return systemPrompt;
        }

        /// <summary>合并固定注入与关键词命中的记忆：按内容去重，固定项在前。</summary>
        private static List<string> MergeMemories(IReadOnlyList<string> important, IReadOnlyList<string> keywordHits)
        {
            var result = new List<string>(important);
            var seen = new HashSet<string>(important, StringComparer.OrdinalIgnoreCase);
            foreach (var hit in keywordHits)
            {
                if (seen.Add(hit))
                {
                    result.Add(hit);
                }
            }

            return result;
        }

        /// <summary>
         /// 模型相关性兜底：带超时，超时或失败都返回空（本次不注入记忆），不阻塞主对话。
         /// </summary>
        private async Task<IReadOnlyList<string>> SearchByLlmWithTimeoutAsync(ModelConfig model, string userMessage, int topK)
        {
            using var cts = new CancellationTokenSource(MemoryJudgeTimeout);
            try
            {
                var hits = await SearchByLlmAsync(model, userMessage, topK, cts.Token);
                _logService.Info($"[记忆] 关键词未命中，模型判断相关性命中 {hits.Count} 条");
                return hits;
            }
            catch (OperationCanceledException)
            {
                _logService.Info($"[记忆] 模型相关性判断超过 {MemoryJudgeTimeout.TotalSeconds:0.#} 秒未返回，本次不注入记忆");
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 模型相关性判断失败，本次不注入记忆：{ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
         /// 模型判断相关性：把长期记忆交给 AI，让它挑出与当前话题相关的记忆（关键词检索的兜底方案）。
         /// </summary>
        private async Task<IReadOnlyList<string>> SearchByLlmAsync(
            ModelConfig model, string userMessage, int topK, CancellationToken cancellationToken)
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

            var reply = await _chatService.SendRawAsync(model, messages, TokenUsageCategory.Memory, cancellationToken);
            return ParseStringArray(reply).Take(topK).ToList();
        }

        /// <summary>
        /// 记录一轮对话：写入短期记忆，溢出到归档，并在归档满时后台触发分析。
        /// </summary>
        public void AddTurn(ModelConfig model, string user, string assistant)
        {
            // 记忆里不允许出现链接：写入短期/归档、以及后续提炼长期记忆前先统一剔除
            assistant = ModelReplyText.StripLinks(assistant);

            var (shortCount, archiveCount, overflowCount, turnEntry) = AppendEntry(user, assistant);

            _logService.Debug($"[记忆] 记录一轮对话：短期 {shortCount}/{ShortMemoryLimit}" +
                              (overflowCount > 0 ? $"，溢出 {overflowCount} 条到归档（归档 {archiveCount}/{ArchiveMemoryLimit}）" : string.Empty));

            // 实时通道：核心信息（生日、姓名、重要偏好等）立即入库，不等归档批处理；
            // 节省模式下走触发词门控，仅命中触发词才发起，避免每轮对话都额外消耗一次模型
            if (!SettingsService.Instance.MemoryExtractionGated || _triggerService.Matches(user))
            {
                // 先登记为「提炼中」，归档分析据此跳过这一轮，避免同一轮被两个通道同时提炼
                lock (_archiveLock)
                {
                    _pendingRealtimeTurns.Add(turnEntry);
                }

                _ = Task.Run(() => RunRealtimeExtractAsync(model, user, assistant, turnEntry));
            }
            else
            {
                _logService.Debug("[记忆] 实时通道：节省模式下未命中触发词，跳过核心信息提取（仍由归档批处理兜底）");
            }

            // 归档满 40 条，后台异步分析；仍在实时提炼中的对话会在分析时被排除并保留
            TryStartArchiveAnalysis(model);
        }

        /// <summary>归档达到上限且当前没有分析在跑时，启动后台分析。</summary>
        private void TryStartArchiveAnalysis(ModelConfig model)
        {
            lock (_archiveLock)
            {
                if (_archiveEntries.Count < ArchiveMemoryLimit)
                {
                    return;
                }
            }

            if (Interlocked.CompareExchange(ref _archiveAnalysisRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(() => RunArchiveAnalysisAsync(model));
        }

        /// <summary>
        /// 记录一句主动搭话：只写短期记忆（溢出仍归档），不触发实时提取与归档分析，避免为一句主动发言额外消耗模型请求。
        /// </summary>
        public void AddProactiveLine(string line)
        {
            var (shortCount, _, overflowCount, _) = AppendEntry("（主动搭话）", line);

            _logService.Debug($"[记忆] 记录主动搭话：短期 {shortCount}/{ShortMemoryLimit}" +
                              (overflowCount > 0 ? $"，溢出 {overflowCount} 条到归档" : string.Empty));
        }

        /// <summary>向短期记忆追加一条记录，满额时溢出到归档并落盘；返回记录后的数量统计与本轮条目。</summary>
        private (int ShortCount, int ArchiveCount, int OverflowCount, ShortMemoryEntry Entry) AppendEntry(string user, string assistant)
        {
            var overflowCount = 0;
            lock (_archiveLock)
            {
                var turnEntry = new ShortMemoryEntry
                {
                    User = user,
                    Assistant = ModelReplyText.StripLinks(assistant)
                };
                _shortEntries.Add(turnEntry);

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
                        Assistant = oldest.Assistant,
                        // 把实时通道的提炼标记带到归档，归档分析据此跳过，避免重复提取
                        RealtimeExtracted = oldest.RealtimeExtracted
                    });
                    overflowCount++;
                }

                _shortStore.Save(_shortEntries);
                _archiveStore.Save(_archiveEntries);
                return (_shortEntries.Count, _archiveEntries.Count, overflowCount, turnEntry);
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
                Interlocked.Exchange(ref _archiveAnalysisRunning, 0);
            }
        }

        /// <summary>实时提取的外层包装：吞掉异常，不影响主流程。</summary>
        private async Task RunRealtimeExtractAsync(
            ModelConfig model, string user, string assistant, ShortMemoryEntry turnEntry)
        {
            try
            {
                _logService.Debug("[记忆] 实时通道：开始提取核心信息...");
                var items = await ExtractCoreInfoAsync(model, user, assistant);
                var coreItems = items.Where(i => i.Importance >= 3).ToList();
                _logService.Debug(coreItems.Count > 0
                    ? $"[记忆] 实时通道：提取到 {coreItems.Count} 条信息"
                    : "[记忆] 实时通道：未发现需要立即记住的信息");
                await StoreItemsAsync(model, coreItems, "实时入库核心信息");

                // 只有真正写入长期记忆的对话才算处理完，归档分析据此跳过、不重复提炼；
                // 没有写入任何信息的对话保持未标记，继续等待归档统一处理（3 分以下的内容也在归档里总结）
                if (coreItems.Count > 0)
                {
                    MarkRealtimeExtracted(turnEntry);
                }

                // 实时入库后同样检查长期记忆上限，避免只在归档分析末尾才压缩
                TryTriggerLongMemoryCompaction(model);
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 实时提取失败：{ex.Message}");
            }
            finally
            {
                // 无论成功、失败还是没提取到内容，都要退出「提炼中」状态，否则归档会一直保留这一轮
                lock (_archiveLock)
                {
                    _pendingRealtimeTurns.Remove(turnEntry);
                }

                TryStartArchiveAnalysis(model);
            }
        }

        /// <summary>标记该轮对话已由实时通道提炼并写入长期记忆，并立即落盘，避免重启后标记丢失。</summary>
        private void MarkRealtimeExtracted(ShortMemoryEntry turnEntry)
        {
            lock (_archiveLock)
            {
                turnEntry.RealtimeExtracted = true;

                // 设置页 Reload 可能已把短期列表换成新的对象，按时间与内容把当前列表里的同一轮也标记上
                foreach (var entry in _shortEntries)
                {
                    if (!entry.RealtimeExtracted && IsSameTurn(entry, turnEntry))
                    {
                        entry.RealtimeExtracted = true;
                    }
                }

                // 该轮可能在实时提炼完成前就已溢出到归档，归档里是复制出来的另一个对象；
                // 必须按时间与内容找到对应副本一起标记，否则归档分析会再提炼一遍
                var propagated = 0;
                foreach (var entry in _archiveEntries)
                {
                    if (entry.IsSummary || entry.RealtimeExtracted || !IsSameTurn(entry, turnEntry))
                    {
                        continue;
                    }

                    entry.RealtimeExtracted = true;
                    propagated++;
                }

                _shortStore.Save(_shortEntries);
                if (propagated > 0)
                {
                    _archiveStore.Save(_archiveEntries);
                    _logService.Debug($"[记忆] 已回写 {propagated} 条归档副本的实时提炼标记");
                }

                _pendingRealtimeTurns.Remove(turnEntry);
            }
        }

        /// <summary>判断归档副本是否对应同一轮短期对话（溢出时时间与内容原样复制）。</summary>
        private static bool IsSameTurn(ArchiveEntry entry, ShortMemoryEntry turn)
        {
            return entry.Time == turn.Time &&
                   string.Equals(entry.User, turn.User, StringComparison.Ordinal) &&
                   string.Equals(entry.Assistant, turn.Assistant, StringComparison.Ordinal);
        }

        /// <summary>判断两条短期记忆是否为同一轮对话（Reload 后对象会被替换，只能按内容匹配）。</summary>
        private static bool IsSameTurn(ShortMemoryEntry left, ShortMemoryEntry right)
        {
            return left.Time == right.Time &&
                   string.Equals(left.User, right.User, StringComparison.Ordinal) &&
                   string.Equals(left.Assistant, right.Assistant, StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断两条归档记录是否为同一条数据。分析期间设置页可能触发 Reload 替换列表对象，
        /// 不能再用引用相等来移除，否则分析完成也删不掉，归档会一直停在 40 条以上。
        /// </summary>
        private static bool IsSameArchiveEntry(ArchiveEntry left, ArchiveEntry right)
        {
            return left.IsSummary == right.IsSummary &&
                   left.Time == right.Time &&
                   string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
                   string.Equals(left.User, right.User, StringComparison.Ordinal) &&
                   string.Equals(left.Assistant, right.Assistant, StringComparison.Ordinal) &&
                   string.Equals(left.Content, right.Content, StringComparison.Ordinal) &&
                   left.Importance == right.Importance;
        }

        /// <summary>
        /// 实时通道：判断本轮对话是否包含值得长期记住的信息（生日、姓名、重要偏好、习惯等），
        /// 提取重要性 ≥ 3 的信息，绕过归档批处理的等待；已处理的对话不会再由归档重复提炼。
        /// </summary>
        private async Task<List<MemoryItem>> ExtractCoreInfoAsync(ModelConfig model, string user, string assistant)
        {
            const string prompt = """
                你是记忆提取器。判断下面这轮对话中是否包含关于用户的、值得长期记住的信息，
                例如：生日、姓名、性别、年龄、住址、联系方式、重要偏好、长期目标、重要关系、重要事件等。

                只提取重要性 ≥ 3 的信息（3 用户习惯，4 用户偏好/长期目标，5 核心用户信息），
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

            var reply = await _chatService.SendRawAsync(
                model, messages, TokenUsageCategory.Memory, kind: ModelCallKind.MemoryExtraction);
            return ParseItems(reply);
        }

        /// <summary>
        /// 归档分析：对归档内容提炼信息点并评分，重要者进长期，其余总结留归档。
        /// </summary>
        private async Task AnalyzeArchiveAsync(ModelConfig model)
        {
            List<ArchiveEntry> snapshot;
            HashSet<ArchiveEntry> pendingEntries;
            lock (_archiveLock)
            {
                snapshot = new List<ArchiveEntry>(_archiveEntries);

                // 仍在实时提炼中的对话不参与本次归档分析，分析结束后原样保留，等标记写回后再处理
                pendingEntries = snapshot
                    .Where(e => !e.IsSummary && _pendingRealtimeTurns.Any(t => IsSameTurn(e, t)))
                    .ToHashSet();
            }

            if (snapshot.Count == 0)
            {
                return;
            }

            _logService.Debug($"[记忆] 归档已满（{snapshot.Count} 条），开始分析筛选..." +
                              (pendingEntries.Count > 0 ? $"，其中 {pendingEntries.Count} 条仍在实时提炼中" : string.Empty));

            // 快照里全部是提炼中的对话时直接让出，避免跑一轮什么都不做的分析
            if (pendingEntries.Count == snapshot.Count)
            {
                _logService.Debug("[记忆] 归档分析暂缓：快照内对话都仍在实时提炼中");
                return;
            }

            var summaryEntries = snapshot.Where(e => e.IsSummary).ToList();

            // 已由实时通道写入长期记忆的对话直接丢弃，不再重复提炼；没有入库的对话照常参与批量分析
            var conversationEntries = snapshot
                .Where(e => !e.IsSummary && !e.RealtimeExtracted && !pendingEntries.Contains(e))
                .ToList();
            var skippedEntries = snapshot.Count(e => !e.IsSummary && e.RealtimeExtracted && !pendingEntries.Contains(e));
            if (skippedEntries > 0)
            {
                _logService.Debug($"[记忆] 归档分析直接丢弃 {skippedEntries} 条已由实时通道入库的对话");
            }

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

            // 3. 入选信息写入长期记忆（疑似重复的条目会先交给模型判定）
            await StoreItemsAsync(model, longTermItems, "已写入长期记忆");

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
                    IsSummary = true
                };
                _logService.Debug("[记忆] 1 条未入选信息暂留归档");
            }

            // 5. 归档重建：移除本轮已处理的快照条目（含直接丢弃的已提炼对话），
            //    保留分析期间新产生的对话和仍在实时提炼中的对话，避免数据丢失。
            //    这里按内容匹配而不是引用：分析期间设置页可能 Reload，列表对象会被整体替换。
            lock (_archiveLock)
            {
                var processed = snapshot
                    .Where(e => !pendingEntries.Contains(e))
                    .ToList();
                var removed = processed.Count == 0
                    ? 0
                    : _archiveEntries.RemoveAll(e => processed.Any(s => IsSameArchiveEntry(e, s)));

                if (remaining != null)
                {
                    _archiveEntries.Add(remaining);
                }
                _archiveStore.Save(_archiveEntries);

                _logService.Debug($"[记忆] 归档已移除 {removed} 条已处理对话，当前剩余 {_archiveEntries.Count} 条");
            }

            _logService.Debug($"[记忆] 归档分析完成：入选 {longTermItems.Count} 条，总结/暂留 {lowItems.Count} 条" +
                              (pendingEntries.Count > 0 ? $"，保留 {pendingEntries.Count} 条提炼中的对话" : string.Empty));

            // 6. 长期记忆满 500 时去重 + 压缩
            TryTriggerLongMemoryCompaction(model);
        }

        /// <summary>
        /// 统一的长期记忆写入入口：先用本地相似度找灰区候选，只有疑似重复的条目才交给模型批量判定；
        /// 判定失败或模型不可用时回落到本地去重，保证记忆不丢。
        /// </summary>
        private async Task StoreItemsAsync(ModelConfig model, IReadOnlyList<MemoryItem> items, string logPrefix)
        {
            if (items.Count == 0)
            {
                return;
            }

            // 用户在「节省模型消耗」页把去重判断设为关闭后，只保留本地相似度去重，不再额外发起模型请求
            if (SettingsService.Instance.GetModelSource(ModelCallKind.MemoryDedupJudge) == ModelCallSource.Disabled)
            {
                _logService.Debug("[记忆] 去重判断已关闭，仅使用本地去重");
                foreach (var item in items)
                {
                    await _memoryManager.StoreAsync(item.Content, item.Importance);
                    _logService.Info($"[记忆] {logPrefix}：{item.Content}（重要性 {item.Importance}）");
                }

                return;
            }

            var plainItems = new List<MemoryItem>();
            var suspects = new List<(MemoryItem Item, IReadOnlyList<MemoryEntry> Candidates)>();
            foreach (var item in items)
            {
                var candidates = _memoryManager.FindNearDuplicateCandidates(
                    item.Content, DedupCandidateMinSimilarity, DedupCandidateMaxCount);

                if (candidates.Count == 0)
                {
                    plainItems.Add(item);
                }
                else
                {
                    suspects.Add((item, candidates));
                }
            }

            foreach (var item in plainItems)
            {
                await _memoryManager.StoreAsync(item.Content, item.Importance);
                _logService.Info($"[记忆] {logPrefix}：{item.Content}（重要性 {item.Importance}）");
            }

            for (var offset = 0; offset < suspects.Count; offset += DedupJudgeBatchSize)
            {
                var chunk = suspects.Skip(offset).Take(DedupJudgeBatchSize).ToList();
                IReadOnlyList<MemoryEntry?> matches;
                try
                {
                    matches = await JudgeDuplicateItemsAsync(model, chunk);
                }
                catch (Exception ex)
                {
                    _logService.Warning($"[记忆] 去重判定失败，回落到本地去重：{ex.Message}");
                    matches = new MemoryEntry?[chunk.Count];
                }

                for (var i = 0; i < chunk.Count; i++)
                {
                    var item = chunk[i].Item;
                    var match = i < matches.Count ? matches[i] : null;
                    if (match != null)
                    {
                        // 模型确认是同一事实：新内容更完整就替换，否则保留旧内容
                        if (_memoryManager.ReplaceIfMoreComplete(match.Id, item.Content, item.Importance))
                        {
                            _logService.Info($"[记忆] 近重复记忆已合并（模型判定同一事实）：{item.Content}");
                        }
                        else if (_memoryManager.ContainsId(match.Id))
                        {
                            _logService.Info($"[记忆] 跳过重复记忆（模型判定同一事实）：{item.Content}");
                        }
                        else
                        {
                            // 判定期间原记忆被压缩或删除，按新记忆写入，避免丢失
                            await _memoryManager.StoreAsync(item.Content, item.Importance);
                            _logService.Info($"[记忆] {logPrefix}：{item.Content}（重要性 {item.Importance}）");
                        }

                        continue;
                    }

                    await _memoryManager.StoreAsync(item.Content, item.Importance);
                    _logService.Info($"[记忆] {logPrefix}：{item.Content}（重要性 {item.Importance}）");
                }
            }
        }

        /// <summary>
        /// 把一批疑似重复的新信息与各自的候选记忆一起交给模型判断是否同一事实。
        /// 返回与入参等长的匹配结果，null 表示不是重复（按新记忆写入）。
        /// </summary>
        private async Task<IReadOnlyList<MemoryEntry?>> JudgeDuplicateItemsAsync(
            ModelConfig model,
            IReadOnlyList<(MemoryItem Item, IReadOnlyList<MemoryEntry> Candidates)> suspects)
        {
            const string prompt = """
                你是记忆去重判断器。判断每条「新信息」是否与「候选记忆」中的某一条描述的是同一件具体事实。
                判定为同一事实：主语和事实相同，只是措辞或详略不同；新信息补充了细节也算同一事实。
                注意：只是同属一个话题但事实不同的，不算同一事实，例如「喜欢 C#」和「喜欢 WPF」是两条不同偏好。

                请只输出一个 JSON 数组，每条新信息对应一项，不要包含其他任何文字或代码块标记：
                [{"n": 1, "same": true, "c": 2}]
                n 是新信息编号；same 为 true 时，c 是最匹配的候选编号；不是同一事实时输出 {"n": 1, "same": false}，不要输出 c。
                """;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("候选记忆：");
            var candidates = new List<MemoryEntry>();
            foreach (var group in suspects)
            {
                foreach (var candidate in group.Candidates)
                {
                    candidates.Add(candidate);
                    sb.AppendLine($"C{candidates.Count}. {candidate.Content}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("新信息：");
            for (var i = 0; i < suspects.Count; i++)
            {
                sb.AppendLine($"N{i + 1}. {suspects[i].Item.Content}");
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = prompt },
                new() { Role = "user", Content = sb.ToString() }
            };

            var reply = await _chatService.SendRawAsync(
                model, messages, TokenUsageCategory.Memory, kind: ModelCallKind.MemoryDedupJudge);

            var matches = new MemoryEntry?[suspects.Count];
            var confirmed = 0;
            foreach (var decision in ParseDedupDecisions(reply))
            {
                var index = decision.NewIndex - 1;
                if (index < 0 || index >= suspects.Count || !decision.Same)
                {
                    continue;
                }

                // 优先采用模型点名的候选；编号越界或不属于该组时回落到该组相似度最高的候选
                MemoryEntry? match = null;
                if (decision.CandidateIndex >= 1 && decision.CandidateIndex <= candidates.Count)
                {
                    var candidate = candidates[decision.CandidateIndex - 1];
                    if (suspects[index].Candidates.Contains(candidate))
                    {
                        match = candidate;
                    }
                }

                match ??= suspects[index].Candidates.FirstOrDefault();
                if (match == null || matches[index] != null)
                {
                    continue;
                }

                matches[index] = match;
                confirmed++;
            }

            if (confirmed > 0)
            {
                _logService.Info($"[记忆] 去重判定：{suspects.Count} 条疑似重复中确认 {confirmed} 条");
            }
            else
            {
                _logService.Debug($"[记忆] 去重判定：{suspects.Count} 条疑似重复均未命中");
            }

            return matches;
        }

        /// <summary>解析去重判定 JSON（失败时返回空列表，后续按非重复处理）。</summary>
        private static List<DedupDecision> ParseDedupDecisions(string reply)
        {
            var result = new List<DedupDecision>();
            var text = StripCodeFence(reply);
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var newIndex = item.TryGetProperty("n", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                    var same = item.TryGetProperty("same", out var s) && s.ValueKind == JsonValueKind.True;
                    var candidateIndex = item.TryGetProperty("c", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
                    if (newIndex > 0)
                    {
                        result.Add(new DedupDecision(newIndex, same, candidateIndex));
                    }
                }
            }
            catch (JsonException)
            {
                // 解析失败按未命中处理，后续回落到本地去重
            }

            return result;
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

        /// <summary>长期记忆达到上限时后台触发去重压缩；已在压缩中或未超限则跳过。</summary>
        private void TryTriggerLongMemoryCompaction(ModelConfig model)
        {
            if (_memoryManager.Count < LongMemoryLimit)
            {
                return;
            }

            // 并发闸门：实时通道与归档分析可能同时走到这里，只允许一个压缩任务在跑
            if (Interlocked.CompareExchange(ref _longCompactionRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(() => RunLongMemoryCompactionAsync(model));
        }

        /// <summary>长期记忆压缩的外层包装：吞掉异常并复位并发闸门。</summary>
        private async Task RunLongMemoryCompactionAsync(ModelConfig model)
        {
            try
            {
                await HandleLongMemoryOverflowAsync(model);
            }
            catch (Exception ex)
            {
                _logService.Warning($"[记忆] 长期记忆整理失败：{ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _longCompactionRunning, 0);
            }
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

            var reply = await _chatService.SendRawAsync(
                model, messages, TokenUsageCategory.Memory, kind: ModelCallKind.MemoryArchiveExtraction);
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

            var reply = await _chatService.SendRawAsync(
                model, messages, TokenUsageCategory.Memory, kind: ModelCallKind.MemoryScoring);
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

            return (await _chatService.SendRawAsync(
                model, messages, TokenUsageCategory.Memory, kind: ModelCallKind.MemorySummary)).Trim();
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

        /// <summary>模型返回的单条去重判定：新信息编号、是否同一事实、匹配的候选编号。</summary>
        private sealed record DedupDecision(int NewIndex, bool Same, int CandidateIndex);
    }
}
