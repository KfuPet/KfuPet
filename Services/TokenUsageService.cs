using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>触发模型调用的来源分类，用于区分 token 消耗的归属。</summary>
    public enum TokenUsageCategory
    {
        /// <summary>主界面聊天对话（含工具调用轮次）。</summary>
        Conversation,

        /// <summary>触摸反应（双击部位、抚摸头部）。</summary>
        TouchReaction,

        /// <summary>拖动越界回正反应。</summary>
        SnapBack,

        /// <summary>主动搭话。</summary>
        ProactiveChat,

        /// <summary>更新通知正文生成。</summary>
        Notification,

        /// <summary>记忆整理（检索、提炼、归档与长期记忆分析等内部调用）。</summary>
        Memory,

        /// <summary>对话后对主人态度的情绪判断。</summary>
        EmotionJudge,

        /// <summary>其他未分类的调用。</summary>
        Other
    }

    /// <summary>界面展示用的 token 用量快照。</summary>
    public sealed class TokenUsageSnapshot
    {
        /// <summary>主对话累计消耗的 token 总量。</summary>
        public long ConversationTokens { get; init; }

        /// <summary>最近一轮对话消耗的 token 总量。</summary>
        public long LastTurnTokens { get; init; }

        /// <summary>其他调用（触摸 / 通知 / 记忆等）累计消耗的 token 总量。</summary>
        public long OtherTokens { get; init; }

        /// <summary>按内置粗略单价估算的累计花费（人民币）。</summary>
        public decimal EstimatedCostCny { get; init; }
    }

    /// <summary>全局 token 用量统计：按分类与模型累计 prompt/completion token，持久化到缓存目录下的 Config\tokenusage.json（默认 %AppData%\KfuPet）。
    /// 金额按内置粗略单价表估算，仅作参考。</summary>
    internal class TokenUsageService
    {
        private static readonly string ConfigFilePath = Path.Combine(AppPaths.ConfigDirectory, "tokenusage.json");

        private static readonly Lazy<TokenUsageService> LazyInstance = new(() => new TokenUsageService());

        /// <summary>全局共享实例。</summary>
        public static TokenUsageService Instance => LazyInstance.Value;

        /// <summary>写盘去抖间隔：短时间内的多次记录只落盘一次，避免频繁写文件。</summary>
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(1500);

        private readonly object _gate = new();
        private readonly object _fileGate = new();
        private readonly Dictionary<string, ModelUsage> _modelUsages = new();
        private readonly Dictionary<TokenUsageCategory, CategoryUsage> _categoryUsages = new();
        private readonly System.Threading.Timer _saveTimer;

        private long _lastConversationTokens;
        private bool _dirty;
        private bool _saveScheduled;

        private TokenUsageService()
        {
            // 构造时同步读一次磁盘（仅启动早期一次，开销很小）
            Load();
            _saveTimer = new System.Threading.Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>记录一次调用的 token 用量；缺失 usage（服务商未返回）时传 0，同样计入请求次数。</summary>
        /// <param name="model">本次调用使用的模型配置。</param>
        /// <param name="category">调用来源分类。</param>
        /// <param name="promptTokens">输入 token 数。</param>
        /// <param name="completionTokens">输出 token 数。</param>
        public void Record(ModelConfig model, TokenUsageCategory category, long promptTokens, long completionTokens)
        {
            if (promptTokens < 0) promptTokens = 0;
            if (completionTokens < 0) completionTokens = 0;

            lock (_gate)
            {
                var key = ModelKey(model.BaseUrl, model.ModelId);
                if (!_modelUsages.TryGetValue(key, out var modelUsage))
                {
                    modelUsage = new ModelUsage { BaseUrl = model.BaseUrl, ModelId = model.ModelId };
                    _modelUsages[key] = modelUsage;
                }
                modelUsage.PromptTokens += promptTokens;
                modelUsage.CompletionTokens += completionTokens;

                if (!_categoryUsages.TryGetValue(category, out var categoryUsage))
                {
                    categoryUsage = new CategoryUsage { Category = category.ToString() };
                    _categoryUsages[category] = categoryUsage;
                }
                categoryUsage.PromptTokens += promptTokens;
                categoryUsage.CompletionTokens += completionTokens;
                categoryUsage.Requests++;

                if (category == TokenUsageCategory.Conversation)
                {
                    _lastConversationTokens += promptTokens + completionTokens;
                }

                _dirty = true;
            }

            ScheduleSave();
        }

        /// <summary>
        /// 开始新一轮对话：清零「上一轮对话消耗」，随后本轮内各次调用（含工具轮次）重新累加。
        /// </summary>
        public void BeginConversationTurn()
        {
            lock (_gate)
            {
                _lastConversationTokens = 0;
                _dirty = true;
            }
        }

        /// <summary>
        /// 恢复初始状态：清空内存中的全部用量统计（磁盘上的 tokenusage.json 由“缓存清理”删除，
        /// 之后有新的调用会重新生成）。清空后不主动落盘，避免把空数据重新写回刚删掉的配置文件。
        /// </summary>
        public void ResetToDefaults()
        {
            lock (_gate)
            {
                _modelUsages.Clear();
                _categoryUsages.Clear();
                _lastConversationTokens = 0;
                _dirty = false;
            }

            Log.Info("[用量] token 统计已清空，恢复初始状态");
        }

        /// <summary>取当前用量快照，供界面展示。</summary>
        public TokenUsageSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                long conversation = 0;
                long other = 0;
                foreach (var entry in _categoryUsages)
                {
                    var total = entry.Value.PromptTokens + entry.Value.CompletionTokens;
                    if (entry.Key == TokenUsageCategory.Conversation)
                    {
                        conversation += total;
                    }
                    else
                    {
                        other += total;
                    }
                }

                var cost = 0m;
                foreach (var modelUsage in _modelUsages.Values)
                {
                    var (inputPrice, outputPrice) = ResolvePrice(modelUsage.BaseUrl, modelUsage.ModelId);
                    cost += modelUsage.PromptTokens / 1_000_000m * inputPrice;
                    cost += modelUsage.CompletionTokens / 1_000_000m * outputPrice;
                }

                return new TokenUsageSnapshot
                {
                    ConversationTokens = conversation,
                    LastTurnTokens = _lastConversationTokens,
                    OtherTokens = other,
                    EstimatedCostCny = cost
                };
            }
        }

        /// <summary>立即把待写数据落盘；退出前调用，避免去抖窗口内最后一次记录丢失。</summary>
        public void Flush()
        {
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Save();
        }

        /// <summary>安排一次延迟落盘；已有待写任务时不重复安排。</summary>
        private void ScheduleSave()
        {
            lock (_gate)
            {
                if (_saveScheduled)
                {
                    return;
                }
                _saveScheduled = true;
            }

            _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
        }

        private void Save()
        {
            TokenUsageData data;
            lock (_gate)
            {
                _saveScheduled = false;
                if (!_dirty)
                {
                    return;
                }
                _dirty = false;

                data = new TokenUsageData
                {
                    Models = _modelUsages.Values.ToList(),
                    Categories = _categoryUsages.Values.ToList(),
                    LastConversationTokens = _lastConversationTokens
                };
            }

            try
            {
                lock (_fileGate)
                {
                    Directory.CreateDirectory(AppPaths.ConfigDirectory);
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigFilePath, json);
                }
            }
            catch (Exception ex)
            {
                // 统计写入失败不影响主流程
                Log.Warning($"[用量] token 统计写入失败：{ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(ConfigFilePath);
                var data = JsonSerializer.Deserialize<TokenUsageData>(json);
                if (data == null)
                {
                    return;
                }

                foreach (var modelUsage in data.Models)
                {
                    if (string.IsNullOrEmpty(modelUsage.BaseUrl) && string.IsNullOrEmpty(modelUsage.ModelId))
                    {
                        continue;
                    }
                    _modelUsages[ModelKey(modelUsage.BaseUrl, modelUsage.ModelId)] = modelUsage;
                }

                foreach (var categoryUsage in data.Categories)
                {
                    if (Enum.TryParse<TokenUsageCategory>(categoryUsage.Category, out var category))
                    {
                        _categoryUsages[category] = categoryUsage;
                    }
                }

                _lastConversationTokens = data.LastConversationTokens;
                Log.Info($"[用量] token 统计已加载：{_modelUsages.Count} 个模型，{_categoryUsages.Count} 类来源");
            }
            catch (Exception ex)
            {
                // 配置缺失或损坏时按空数据启动，避免影响启动
                Log.Warning($"[用量] token 统计读取失败，按空数据启动：{ex.Message}");
            }
        }

        private static string ModelKey(string baseUrl, string modelId) => baseUrl + "|" + modelId;

        /// <summary>
        /// 按服务商 / 模型粗略估算单价（人民币 / 每百万 token）。
        /// 价格随时可能调整，这里只取大致量级用于「约花费」展示，未知模型按通用价估算。
        /// </summary>
        private static (decimal Input, decimal Output) ResolvePrice(string baseUrl, string modelId)
        {
            var id = (modelId ?? string.Empty).ToLowerInvariant();

            if (id.Contains("reasoner") || id.Contains("r1"))
            {
                return (4m, 16m);
            }
            if (id.Contains("deepseek"))
            {
                return (2m, 8m);
            }
            if (id.Contains("moonshot") || id.Contains("kimi"))
            {
                return (12m, 12m);
            }
            if (id.Contains("gpt-4o"))
            {
                return (18m, 72m);
            }

            var host = string.Empty;
            if (Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri))
            {
                host = uri.Host.ToLowerInvariant();
            }
            if (host.Contains("deepseek"))
            {
                return (2m, 8m);
            }
            if (host.Contains("moonshot"))
            {
                return (12m, 12m);
            }

            // 通用兜底价：取常见量级，保证「约花费」不为零
            return (2m, 8m);
        }

        /// <summary>单个模型的累计用量。</summary>
        private sealed class ModelUsage
        {
            public string BaseUrl { get; set; } = string.Empty;
            public string ModelId { get; set; } = string.Empty;
            public long PromptTokens { get; set; }
            public long CompletionTokens { get; set; }
        }

        /// <summary>单类来源的累计用量。</summary>
        private sealed class CategoryUsage
        {
            public string Category { get; set; } = string.Empty;
            public long PromptTokens { get; set; }
            public long CompletionTokens { get; set; }
            public int Requests { get; set; }
        }

        /// <summary>落盘数据结构。</summary>
        private sealed class TokenUsageData
        {
            public List<ModelUsage> Models { get; set; } = new();
            public List<CategoryUsage> Categories { get; set; } = new();
            public long LastConversationTokens { get; set; }
        }
    }
}