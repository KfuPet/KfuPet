using System.Text.Json;
using KfuPet.Models;
using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    /// <summary>会引发情绪变化的事件类型。</summary>
    internal enum EmotionEventKind
    {
        /// <summary>抚摸头部（按住头部来回滑动）。</summary>
        Petting,

        /// <summary>双击部位。</summary>
        Touch,

        /// <summary>拖动桌宠：只刷新互动时间，不改变数值，避免靠拖动刷亲密度。</summary>
        Drag,

        /// <summary>主人发来一条消息。</summary>
        Chat,

        /// <summary>被拖出屏幕后回正。</summary>
        SnapBack
    }

    /// <summary>情感服务：本地维护愉悦/精力/亲密三维度（0–100），支持事件增减、随时间衰减与落盘持久化。
    /// 不做轮询——时间结算惰性化，在读/写前按时间游标补算，不占定时器也不唤醒 UI 线程；情绪上下文可注入系统提示词并影响主动搭话概率。</summary>
    internal class EmotionService : ICommandService
    {
        // ── 基线 ───────────────────────────────────────
        private const double JoyBaseline = 55;
        private const double EnergyBaseline = 60;
        private const double IntimacyBaseline = 50;

        // ── 衰减时间常数（分钟）：偏离基线越远，回正越快 ──────────
        private const double JoyDecayMinutes = 180;
        private const double EnergyDecayMinutes = 90;
        private const double IntimacyDecayMinutes = 1440;

        // ── 长时间没人互动：情绪缓慢下滑 ───────────────────────
        // 只管两小时以上的长时间离开；短时间的"它开口了你没理"交给下面的"被无视"机制，
        // 两者职责错开，不会对同一次冷落重复扣分。
        private const double NeglectAfterMinutes = 120;
        private const double NeglectPenaltyCapMinutes = 240;
        private const double NeglectJoyPerHour = 12;
        private const double NeglectIntimacyPerHour = 3;

        // ── 被无视：主动搭话后规定时间内没得到任何回应 ─────────────
        private const double IgnoredJoyPenalty = 4;
        private const double IgnoredIntimacyPenalty = 3;

        /// <summary>连续被无视时，每次扣分在上一次基础上上浮的比例。</summary>
        private const double IgnoredStreakStep = 0.5;

        /// <summary>连续被无视时扣分倍数的上限（基础值的 3 倍）。</summary>
        private const double IgnoredMaxFactor = 3.0;

        // ── 单次事件的增减量 ─────────────────────────────
        private const double PettingJoy = 3;
        private const double PettingEnergy = -1;
        private const double PettingIntimacy = 2;

        private const double TouchJoy = 1.5;
        private const double TouchEnergy = -2;
        private const double TouchIntimacy = 0.5;

        private const double ChatJoy = 2;
        private const double ChatEnergy = 1;
        private const double ChatIntimacy = 2;

        private const double SnapBackJoy = -3;
        private const double SnapBackEnergy = -2;
        private const double SnapBackIntimacy = -1;

        /// <summary>单次调整（模型工具 / 命令通道）每个维度允许的最大幅度。</summary>
        private const double MaxSingleAdjust = 15;

        /// <summary>落盘节流：两次自动保存之间的最短间隔。</summary>
        private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);

        private static readonly string StateFilePath = Path.Combine(AppPaths.ConfigDirectory, "emotion.json");

        private static readonly Lazy<EmotionService> LazyInstance = new(() => new EmotionService());

        /// <summary>全局共享实例（提示词注入、主动搭话与设置界面共用）。</summary>
        public static EmotionService Instance => LazyInstance.Value;

        private readonly object _sync = new();

        private EmotionState _state;
        private bool _dirty;
        private DateTime _lastSaveUtc = DateTime.MinValue;

        private EmotionService()
        {
            _state = CreateDefaultState();
            LoadLocked();
        }

        /// <summary>情绪发生变化（事件增减、模型调整或复位）时触发，用于让设置界面实时刷新。</summary>
        public event EventHandler? Changed;

        /// <summary>
        /// 情绪被主动调整（模型工具或命令通道）的累计次数。
        /// 主对话在发送前后比对该值，判断本轮是否已由模型工具调整过情绪，从而跳过对话后的重复判断。
        /// </summary>
        public long AdjustCount { get; private set; }

        /// <summary>当前愉悦度（0–100），读取时先结算流逝的时间。</summary>
        public double Joy
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return _state.Joy;
                }
            }
        }

        /// <summary>当前精力（0–100）。</summary>
        public double Energy
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return _state.Energy;
                }
            }
        }

        /// <summary>当前亲密度（0–100）。</summary>
        public double Intimacy
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return _state.Intimacy;
                }
            }
        }

        /// <summary>当前愉悦度的状态词（如“心情不错”“有点低落”）。</summary>
        public string MoodWord
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return JoyWord(_state.Joy);
                }
            }
        }

        /// <summary>当前精力的状态词（如“精神满满”“困得不行”）。</summary>
        public string EnergyMoodWord
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return EnergyWord(_state.Energy);
                }
            }
        }

        /// <summary>当前亲密度的状态词（如“特别黏你”“有点生疏”）。</summary>
        public string IntimacyMoodWord
        {
            get
            {
                lock (_sync)
                {
                    ApplyElapsedLocked();
                    return IntimacyWord(_state.Intimacy);
                }
            }
        }

        public string ServiceName => "emotion";

        /// <summary>主人产生了互动：刷新“被冷落”计时，并按事件类型调整情绪。</summary>
        /// <param name="kind">互动类型。</param>
        /// <param name="reason">用于日志的中文说明。</param>
        public void OnUserInteraction(EmotionEventKind kind, string reason)
        {
            double deltaJoy, deltaEnergy, deltaIntimacy;
            double joy, energy, intimacy;

            lock (_sync)
            {
                ApplyElapsedLocked();

                switch (kind)
                {
                    case EmotionEventKind.Petting:
                        deltaJoy = PettingJoy;
                        deltaEnergy = PettingEnergy;
                        deltaIntimacy = PettingIntimacy;
                        break;

                    case EmotionEventKind.Touch:
                        deltaJoy = TouchJoy;
                        deltaEnergy = TouchEnergy;
                        deltaIntimacy = TouchIntimacy;
                        break;

                    case EmotionEventKind.Chat:
                        deltaJoy = ChatJoy;
                        deltaEnergy = ChatEnergy;
                        deltaIntimacy = ChatIntimacy;
                        break;

                    case EmotionEventKind.SnapBack:
                        deltaJoy = SnapBackJoy;
                        deltaEnergy = SnapBackEnergy;
                        deltaIntimacy = SnapBackIntimacy;
                        break;

                    case EmotionEventKind.Drag:
                    default:
                        // 主人只是挪动窗口，只刷新互动时间，不给数值加成
                        deltaJoy = 0;
                        deltaEnergy = 0;
                        deltaIntimacy = 0;
                        break;
                }

                _state.Joy = Math.Clamp(_state.Joy + deltaJoy, 0, 100);
                _state.Energy = Math.Clamp(_state.Energy + deltaEnergy, 0, 100);
                _state.Intimacy = Math.Clamp(_state.Intimacy + deltaIntimacy, 0, 100);
                _state.LastInteractionUtc = DateTime.UtcNow;
                _dirty = true;

                joy = _state.Joy;
                energy = _state.Energy;
                intimacy = _state.Intimacy;
            }

            Log.Debug($"[情绪] {reason}（{kind}）→ 愉悦 {joy:F0}、精力 {energy:F0}、亲密 {intimacy:F0}");
            RaiseChanged();
            Save();
        }

        /// <summary>按给定增量调整情绪（供模型工具与命令通道使用），每个维度单次钳制在 ±15 以内。</summary>
        /// <param name="reason">用于日志的中文说明。</param>
        public void Adjust(double joyDelta, double energyDelta, double intimacyDelta, string reason)
        {
            double joy, energy, intimacy;

            lock (_sync)
            {
                ApplyElapsedLocked();

                _state.Joy = Math.Clamp(_state.Joy + Math.Clamp(joyDelta, -MaxSingleAdjust, MaxSingleAdjust), 0, 100);
                _state.Energy = Math.Clamp(_state.Energy + Math.Clamp(energyDelta, -MaxSingleAdjust, MaxSingleAdjust), 0, 100);
                _state.Intimacy = Math.Clamp(_state.Intimacy + Math.Clamp(intimacyDelta, -MaxSingleAdjust, MaxSingleAdjust), 0, 100);
                _dirty = true;
                AdjustCount++;

                joy = _state.Joy;
                energy = _state.Energy;
                intimacy = _state.Intimacy;
            }

            Log.Info($"[情绪] {reason} → 愉悦 {joy:F0}、精力 {energy:F0}、亲密 {intimacy:F0}");
            RaiseChanged();
            Save();
        }

        /// <summary>主动搭话后规定时间内无任何互动（被无视）：按连续次数加重下调愉悦与亲密，精力不受影响。
        /// 刻意不计入 <see cref="AdjustCount"/>——这是被动惩罚而非模型对主人态度的判断，计入会让同轮对话误判为"已调整过"而跳过态度判断。</summary>
        /// <param name="streak">连续被无视的次数，从 1 开始；被回应一次即清零。</param>
        public void OnIgnored(int streak)
        {
            var times = Math.Max(streak, 1);
            var factor = Math.Min(1 + (times - 1) * IgnoredStreakStep, IgnoredMaxFactor);
            double joy, energy, intimacy;

            lock (_sync)
            {
                ApplyElapsedLocked();

                _state.Joy = Math.Clamp(_state.Joy - IgnoredJoyPenalty * factor, 0, 100);
                _state.Intimacy = Math.Clamp(_state.Intimacy - IgnoredIntimacyPenalty * factor, 0, 100);
                _dirty = true;

                joy = _state.Joy;
                energy = _state.Energy;
                intimacy = _state.Intimacy;
            }

            Log.Info($"[情绪] 连续第 {times} 次被无视（扣分 ×{factor:F1}）→ " +
                     $"愉悦 {joy:F0}、精力 {energy:F0}、亲密 {intimacy:F0}");
            RaiseChanged();
            Save();
        }

        /// <summary>
        /// 构建注入系统提示词的情绪上下文：只给状态词与行为要求，不给原始数值，
        /// 避免模型在回复里复述数字。
        /// </summary>
        public string BuildPromptContext()
        {
            double joy, energy, intimacy;

            lock (_sync)
            {
                ApplyElapsedLocked();
                joy = _state.Joy;
                energy = _state.Energy;
                intimacy = _state.Intimacy;
            }

            return $"\n\n你现在的心情：{JoyWord(joy)}，{EnergyWord(energy)}，{IntimacyWord(intimacy)}。" +
                   "回应时让语气自然带上这份心情，不要直接说出或解释这些状态词。";
        }

        /// <summary>
        /// 主动搭话概率的乘数因子：越亲近越爱开口；被冷落到心情低落时反而更少搭话（闹别扭）。
        /// 取值范围 0.3–2.0。
        /// </summary>
        public double GetProactiveFactor()
        {
            double joy, energy, intimacy;

            lock (_sync)
            {
                ApplyElapsedLocked();
                joy = _state.Joy;
                energy = _state.Energy;
                intimacy = _state.Intimacy;
            }

            var joyFactor = joy < 40 ? 0.8 : joy >= 70 ? 1.15 : 1.0;
            var energyFactor = energy < 35 ? 0.75 : 1.0;
            var factor = (0.45 + intimacy / 100 * 0.75) * joyFactor * energyFactor;
            return Math.Clamp(factor, 0.3, 2.0);
        }

        /// <summary>把情绪状态写入 emotion.json；自动调用受节流限制，<paramref name="force"/> 为 true 时立即写入。</summary>
        /// <param name="force">是否跳过节流立即落盘（关闭窗口时使用）。</param>
        public void Save(bool force = false)
        {
            lock (_sync)
            {
                if (!_dirty && !force)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                if (!force && now - _lastSaveUtc < SaveInterval)
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(AppPaths.ConfigDirectory);
                    var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(StateFilePath, json);
                    _lastSaveUtc = now;
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    // 保存失败不阻塞使用，下次启动回退到基线情绪
                    Log.Error($"[情绪] 保存失败：{ex.Message}");
                }
            }
        }

        /// <summary>从磁盘重新载入情绪状态：离线期间只结算衰减，不追罚“被冷落”。</summary>
        public void Reload()
        {
            lock (_sync)
            {
                _state = CreateDefaultState();
                LoadLocked();
                _dirty = false;
                _lastSaveUtc = DateTime.MinValue;
            }

            RaiseChanged();
        }

        /// <summary>恢复初始情绪：内存回到基线并删除磁盘状态文件（供“清理软件配置”使用）。</summary>
        public void ResetToDefaults()
        {
            lock (_sync)
            {
                _state = CreateDefaultState();
                _dirty = false;
                _lastSaveUtc = DateTime.MinValue;

                try
                {
                    if (File.Exists(StateFilePath))
                    {
                        File.Delete(StateFilePath);
                    }
                }
                catch (Exception ex)
                {
                    // 删除失败不影响内存中的复位
                    Log.Warning($"[情绪] 状态文件删除失败：{ex.Message}");
                }
            }

            Log.Info("[情绪] 已恢复初始情绪状态");
            RaiseChanged();
        }

        public CommandResponse Execute(string action, Dictionary<string, object>? parameters)
        {
            switch (action.ToLowerInvariant())
            {
                case "get":
                    return CommandResponse.Ok(new
                    {
                        joy = Joy,
                        energy = Energy,
                        intimacy = Intimacy,
                        mood = MoodWord
                    });

                case "set":
                    return SetValues(parameters);

                case "adjust":
                    Adjust(
                        Dbl(parameters, "joy"),
                        Dbl(parameters, "energy"),
                        Dbl(parameters, "intimacy"),
                        Str(parameters, "reason", "外部调整"));
                    return CommandResponse.Ok(new { joy = Joy, energy = Energy, intimacy = Intimacy });

                case "notify":
                    NotifyInteraction();
                    return CommandResponse.Ok(new { joy = Joy, energy = Energy, intimacy = Intimacy });

                case "reset":
                    ResetToDefaults();
                    return CommandResponse.Ok();

                default:
                    return CommandResponse.Fail($"未知操作：{action}");
            }
        }

        /// <summary>直接设置指定维度的数值（未提供的维度保持原值），供外部工具调试。</summary>
        private CommandResponse SetValues(Dictionary<string, object>? parameters)
        {
            lock (_sync)
            {
                ApplyElapsedLocked();

                if (parameters?.ContainsKey("joy") == true)
                {
                    _state.Joy = Math.Clamp(Dbl(parameters, "joy"), 0, 100);
                }

                if (parameters?.ContainsKey("energy") == true)
                {
                    _state.Energy = Math.Clamp(Dbl(parameters, "energy"), 0, 100);
                }

                if (parameters?.ContainsKey("intimacy") == true)
                {
                    _state.Intimacy = Math.Clamp(Dbl(parameters, "intimacy"), 0, 100);
                }

                _dirty = true;
            }

            RaiseChanged();
            Save();
            return CommandResponse.Ok(new { joy = Joy, energy = Energy, intimacy = Intimacy });
        }

        /// <summary>只刷新“上次互动”时间（不改变数值），用于外部通道声明主人有互动。</summary>
        private void NotifyInteraction()
        {
            lock (_sync)
            {
                ApplyElapsedLocked();
                _state.LastInteractionUtc = DateTime.UtcNow;
                _dirty = true;
            }

            Log.Debug("[情绪] 收到互动通知，已刷新被冷落计时");
        }

        /// <summary>惰性结算：把上次结算到现在的衰减与“被冷落”惩罚补算进当前值。</summary>
        private void ApplyElapsedLocked()
        {
            var now = DateTime.UtcNow;

            if (_state.UpdatedAtUtc == default)
            {
                _state.UpdatedAtUtc = now;
                _state.LastInteractionUtc = now;
                return;
            }

            var elapsedMinutes = (now - _state.UpdatedAtUtc).TotalMinutes;
            if (elapsedMinutes <= 0)
            {
                return;
            }

            _state.Joy = Decay(_state.Joy, JoyBaseline, elapsedMinutes, JoyDecayMinutes);
            _state.Energy = Decay(_state.Energy, EnergyBaseline, elapsedMinutes, EnergyDecayMinutes);
            _state.Intimacy = Decay(_state.Intimacy, IntimacyBaseline, elapsedMinutes, IntimacyDecayMinutes);

            // 惩罚按“已计入的闲置时长增量”计算，保证重复结算不会重复扣分
            var creditedDelta = CreditedIdleMinutes(now) - CreditedIdleMinutes(_state.UpdatedAtUtc);
            if (creditedDelta > 0)
            {
                var hours = creditedDelta / 60;
                _state.Joy -= NeglectJoyPerHour * hours;
                _state.Intimacy -= NeglectIntimacyPerHour * hours;
            }

            ClampAllLocked();
            _state.UpdatedAtUtc = now;
        }

        /// <summary>只做衰减，不结算“被冷落”惩罚（离线期间情绪自然平复，不追罚）。</summary>
        private void ApplyDecayOnlyLocked(DateTime now)
        {
            var elapsedMinutes = (now - _state.UpdatedAtUtc).TotalMinutes;
            if (elapsedMinutes <= 0)
            {
                return;
            }

            _state.Joy = Decay(_state.Joy, JoyBaseline, elapsedMinutes, JoyDecayMinutes);
            _state.Energy = Decay(_state.Energy, EnergyBaseline, elapsedMinutes, EnergyDecayMinutes);
            _state.Intimacy = Decay(_state.Intimacy, IntimacyBaseline, elapsedMinutes, IntimacyDecayMinutes);
            ClampAllLocked();
        }

        /// <summary>某个时刻“已经计入惩罚的闲置分钟数”，超过起始阈值后开始累计，并封顶避免关机多天后被砸穿。</summary>
        private double CreditedIdleMinutes(DateTime cursor)
        {
            if (_state.LastInteractionUtc == default)
            {
                return 0;
            }

            var idleMinutes = (cursor - _state.LastInteractionUtc).TotalMinutes - NeglectAfterMinutes;
            return Math.Clamp(idleMinutes, 0, NeglectPenaltyCapMinutes);
        }

        /// <summary>指数衰减：按经过的时间把数值拉回基线。</summary>
        private static double Decay(double value, double baseline, double elapsedMinutes, double tauMinutes)
        {
            if (elapsedMinutes <= 0 || tauMinutes <= 0)
            {
                return value;
            }

            return baseline + (value - baseline) * Math.Exp(-elapsedMinutes / tauMinutes);
        }

        private void ClampAllLocked()
        {
            _state.Joy = Math.Clamp(_state.Joy, 0, 100);
            _state.Energy = Math.Clamp(_state.Energy, 0, 100);
            _state.Intimacy = Math.Clamp(_state.Intimacy, 0, 100);
        }

        private static EmotionState CreateDefaultState()
        {
            var now = DateTime.UtcNow;
            return new EmotionState
            {
                Joy = JoyBaseline,
                Energy = EnergyBaseline,
                Intimacy = IntimacyBaseline,
                UpdatedAtUtc = now,
                LastInteractionUtc = now
            };
        }

        private void LoadLocked()
        {
            var now = DateTime.UtcNow;

            try
            {
                if (File.Exists(StateFilePath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(StateFilePath));
                    var root = document.RootElement;

                    _state.Joy = ReadNumber(root, "Joy", JoyBaseline);
                    _state.Energy = ReadNumber(root, "Energy", EnergyBaseline);
                    _state.Intimacy = ReadNumber(root, "Intimacy", IntimacyBaseline);
                    _state.UpdatedAtUtc = ReadUtc(root, "UpdatedAtUtc") ?? now;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[情绪] 状态读取失败，改用基线情绪：{ex.Message}");
            }

            // 重新打开软件即视为主人回来了：离线期间只结算衰减，不追罚，并从此刻起重新计互动
            ApplyDecayOnlyLocked(now);
            _state.UpdatedAtUtc = now;
            _state.LastInteractionUtc = now;
            ClampAllLocked();

            Log.Info($"[情绪] 已载入：愉悦 {_state.Joy:F0}、精力 {_state.Energy:F0}、亲密 {_state.Intimacy:F0}");
        }

        private static double ReadNumber(JsonElement root, string name, double fallback)
        {
            return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : fallback;
        }

        private static DateTime? ReadUtc(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            try
            {
                return element.GetDateTime().ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>愉悦度状态词。</summary>
        private static string JoyWord(double value) => value switch
        {
            >= 80 => "超开心",
            >= 60 => "心情不错",
            >= 40 => "还算平静",
            >= 20 => "有点低落",
            _ => "很难过"
        };

        /// <summary>精力状态词。</summary>
        private static string EnergyWord(double value) => value switch
        {
            >= 75 => "精神满满",
            >= 45 => "还算有精神",
            >= 25 => "有点没精神",
            _ => "困得不行"
        };

        /// <summary>亲密度状态词。</summary>
        private static string IntimacyWord(double value) => value switch
        {
            >= 75 => "特别黏你",
            >= 50 => "跟你挺亲近",
            >= 30 => "有点想你了",
            _ => "有点生疏"
        };

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static string Str(Dictionary<string, object>? parameters, string key, string fallback)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value))
            {
                return fallback;
            }

            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static double Dbl(Dictionary<string, object>? parameters, string key, double fallback = 0)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble();
            }

            return double.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
        }
    }
}
