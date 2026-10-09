using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace KfuPet.Services
{
    /// <summary>主动搭话的触发类型。</summary>
    internal enum ProactiveTriggerType
    {
        /// <summary>独处搭话：在电脑前但有一阵子没和桌宠互动。</summary>
        IdleChat,

        /// <summary>欢迎回来：离开较久后刚回到电脑前。</summary>
        WelcomeBack,

        /// <summary>早上问候。</summary>
        MorningGreeting,

        /// <summary>晚上问候。</summary>
        EveningGreeting
    }

    /// <summary>主动搭话触发事件参数：触发类型、角色包事件键与动态语境。</summary>
    internal sealed class ProactiveTriggerEventArgs : EventArgs
    {
        public ProactiveTriggerEventArgs(ProactiveTriggerType type, string eventKey, string contextNote)
        {
            Type = type;
            EventKey = eventKey;
            ContextNote = contextNote;
        }

        /// <summary>触发类型。</summary>
        public ProactiveTriggerType Type { get; }

        /// <summary>角色包 reactions.json 中的事件键（idleChat / welcomeBack / morningGreeting / eveningGreeting）。</summary>
        public string EventKey { get; }

        /// <summary>动态语境（当前时间、间隔时长等），由主窗口拼进发给 AI 的事件消息。</summary>
        public string ContextNote { get; }
    }

    /// <summary>
    /// 主动搭话服务：用低频心跳按"闸门链"判断什么时候可以让桌宠主动开口。
    /// 心跳只做本地判断（设置、安静时段、冷却、系统空闲检测、界面忙闲），不产生模型请求；
    /// 全部闸门通过时抛出 <see cref="Triggered"/> 事件，由主窗口负责生成台词并显示。
    /// 台词与事件描述完全来自角色包，代码不内置文案。
    /// </summary>
    internal class ProactiveChatService
    {
        // ── 心跳与阈值 ─────────────────────────────

        /// <summary>心跳间隔（秒）：本地检查，不产生模型请求。</summary>
        private const int HEARTBEAT_SECONDS = 30;

        /// <summary>系统空闲达到该时长视为"主人离开"（秒）。</summary>
        private const int AWAY_IDLE_SECONDS = 900;

        /// <summary>系统空闲降到该值以下视为"主人回来了"（秒）。</summary>
        private const int RETURN_IDLE_SECONDS = 5;

        /// <summary>欢迎判定宽限（分钟）：回来后的这段时间内开口，超过或已先来互动则取消本次欢迎。</summary>
        private const int WELCOME_GRACE_MINUTES = 3;

        /// <summary>欢迎回来的独立冷却（小时）：防止反复离开/回来刷屏。</summary>
        private const int WELCOME_COOLDOWN_HOURS = 2;

        /// <summary>任意两次主动搭话之间的最小间隔（分钟）。</summary>
        private const int MIN_GAP_MINUTES = 10;

        // ── 定时问候窗口（一天内的分钟数）────────────

        /// <summary>早上问候窗口起点：8:00。</summary>
        private const int MORNING_WINDOW_START_MINUTE = 8 * 60;

        /// <summary>早上问候窗口终点：10:00。</summary>
        private const int MORNING_WINDOW_END_MINUTE = 10 * 60;

        /// <summary>晚上问候窗口起点：20:00。</summary>
        private const int EVENING_WINDOW_START_MINUTE = 20 * 60;

        /// <summary>晚上问候窗口终点：22:30。</summary>
        private const int EVENING_WINDOW_END_MINUTE = 22 * 60 + 30;

        /// <summary>问候目标时刻随机落在窗口前段的占比：避免"窗口一开始就准时报到"。</summary>
        private const double GREETING_TARGET_RANDOM_RATIO = 0.6;

        // ── 频度档位 ───────────────────────────────

        /// <summary>频度档位参数：独处判定阈值（距上次互动的分钟数）、冷却（分钟）与每次心跳的触发概率。</summary>
        private readonly record struct FrequencyProfile(int IdleThresholdMinutes, int CooldownMinutes, double Probability);

        // ── 状态 ───────────────────────────────────

        private DispatcherTimer? _heartbeat;

        /// <summary>界面忙闲查询：可以说话时返回 null，否则返回不能说话的原因（如"正在发送消息"），仅用于日志。</summary>
        private Func<string?>? _busyStateQuery;

        /// <summary>距上次"主人与桌宠互动"的时间（触摸 / 发消息 / 拖动窗口）。</summary>
        private DateTime _lastPetInteractionTime;

        /// <summary>上次主动搭话触发时间。</summary>
        private DateTime _lastProactiveTime;

        /// <summary>上次欢迎回来的时间。</summary>
        private DateTime _lastWelcomeTime = DateTime.MinValue;

        /// <summary>是否处于"主人离开"状态。</summary>
        private bool _wasAway;

        /// <summary>检测到离开的时刻，用于判断回来后是否已经先互动过。</summary>
        private DateTime _awayDetectedTime;

        /// <summary>本次离开的实际开始时刻（由空闲时长回推），用于报告离开时长。</summary>
        private DateTime _awaySince;

        /// <summary>欢迎判定宽限截止时间；为空表示当前没有待判定的欢迎。</summary>
        private DateTime? _pendingWelcomeUntil;

        /// <summary>最后一次早上/晚上问候的日期。</summary>
        private DateOnly? _morningGreetedDate;

        private DateOnly? _eveningGreetedDate;

        /// <summary>本次窗口随机挑出的问候目标时刻。</summary>
        private DateTime? _morningTargetTime;

        private DateTime? _eveningTargetTime;

        /// <summary>触发事件：闸门全过时抛出，由主窗口负责生成台词并显示。</summary>
        public event EventHandler<ProactiveTriggerEventArgs>? Triggered;

        /// <summary>
        /// 启动心跳。describeBusyState 用于向主窗口查询界面此刻是否适合说话：
        /// 可以说话返回 null，否则返回原因文案（仅用于日志）。
        /// 启动时把"最后互动/最后主动"初始化为当前时刻，最初一个冷却周期内不会开口。
        /// </summary>
        public void Start(Func<string?> describeBusyState)
        {
            Stop();

            _busyStateQuery = describeBusyState;
            var now = DateTime.Now;
            _lastPetInteractionTime = now;
            _lastProactiveTime = now;
            _lastWelcomeTime = DateTime.MinValue;
            _wasAway = false;
            _pendingWelcomeUntil = null;
            _morningGreetedDate = null;
            _eveningGreetedDate = null;
            _morningTargetTime = null;
            _eveningTargetTime = null;

            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) };
            _heartbeat.Tick += OnHeartbeatTick;
            _heartbeat.Start();

            // 启动摘要：把生效中的设置记进日志，便于对照排查
            var settings = SettingsService.Instance;
            var quietText = settings.QuietHoursStart == settings.QuietHoursEnd
                ? "未启用"
                : $"{settings.QuietHoursStart:00}:00–{settings.QuietHoursEnd:00}:00";
            Log.Info($"[主动搭话] 心跳已启动：间隔 {HEARTBEAT_SECONDS} 秒｜" +
                     $"总开关{(settings.ProactiveChatEnabled ? "开" : "关")}｜频率 {settings.ProactiveChatFrequency}｜" +
                     $"安静时段 {quietText}｜触发类型 独处{(settings.ProactiveIdleEnabled ? "开" : "关")}/" +
                     $"欢迎{(settings.ProactiveWelcomeBackEnabled ? "开" : "关")}/问候{(settings.ProactiveGreetingEnabled ? "开" : "关")}");
        }

        /// <summary>停止心跳（窗口关闭时调用）。</summary>
        public void Stop()
        {
            if (_heartbeat == null)
            {
                return;
            }

            _heartbeat.Stop();
            _heartbeat.Tick -= OnHeartbeatTick;
            _heartbeat = null;
        }

        /// <summary>记录一次"主人与桌宠互动"（触摸、发消息、拖动窗口），用于独处判定。</summary>
        public void NotifyPetInteraction()
        {
            var now = DateTime.Now;
            Log.Debug($"[主动搭话] 记录主人互动（距上次互动 {(now - _lastPetInteractionTime).TotalMinutes:F1} 分钟）");
            _lastPetInteractionTime = now;
        }

        private void OnHeartbeatTick(object? sender, EventArgs e)
        {
            try
            {
                Evaluate();
            }
            catch (Exception ex)
            {
                // 心跳异常不影响桌宠使用，记录完整异常后等待下一次心跳
                Log.Warning($"[主动搭话] 心跳检查异常：{ex}");
            }
        }

        /// <summary>一次心跳的完整判定：状态更新 → 共用闸门 → 按优先级检查三类触发；每一步结果都记录日志。</summary>
        private void Evaluate()
        {
            var now = DateTime.Now;
            var settings = SettingsService.Instance;

            // 先更新离开/回来状态（欢迎判定依赖它）
            var idleSeconds = GetSystemIdleSeconds();
            if (UpdateAwayState(now, idleSeconds))
            {
                Log.Debug($"[主动搭话] 检测到主人回到电脑前（空闲 {idleSeconds:F0} 秒）");
            }

            var status = BuildStatusText(now, idleSeconds);

            // 共用闸门：总开关 → 安静时段 → 界面忙闲
            if (!settings.ProactiveChatEnabled)
            {
                Log.Debug($"[主动搭话] {status}｜跳过：总开关已关闭");
                return;
            }
            if (IsQuietHour(now, settings))
            {
                Log.Debug($"[主动搭话] {status}｜跳过：安静时段 {settings.QuietHoursStart:00}:00–{settings.QuietHoursEnd:00}:00");
                return;
            }

            var busyReason = _busyStateQuery?.Invoke();
            if (busyReason != null)
            {
                Log.Debug($"[主动搭话] {status}｜跳过：界面忙（{busyReason}）");
                return;
            }

            // 1) 欢迎回来：回来后的宽限期内判定一次，不掷概率
            if (TryWelcomeBack(now, settings))
            {
                return;
            }

            // 2) 定时问候：早/晚窗口内按随机目标时刻触发，每天各一次
            if (settings.ProactiveGreetingEnabled && !_wasAway)
            {
                var greeting = CheckGreeting(now);
                if (greeting.HasValue)
                {
                    if ((now - _lastProactiveTime).TotalMinutes >= MIN_GAP_MINUTES)
                    {
                        Log.Debug($"[主动搭话] {status}｜触发：{greeting.Value}");
                        Fire(greeting.Value, now);
                        return;
                    }

                    Log.Debug($"[主动搭话] {status}｜问候已就绪，但距上次开口不足 {MIN_GAP_MINUTES} 分钟");
                }
            }

            // 3) 独处搭话：人在电脑前、距上次互动超过阈值后按概率掷骰
            if (!settings.ProactiveIdleEnabled)
            {
                Log.Debug($"[主动搭话] {status}｜跳过：独处搭话开关已关闭");
                return;
            }
            if (_wasAway)
            {
                Log.Debug($"[主动搭话] {status}｜跳过：主人处于离开状态，等待回来时机");
                return;
            }

            var profile = GetProfile(settings.ProactiveChatFrequency);
            var idleMinutes = (now - _lastPetInteractionTime).TotalMinutes;
            var sinceLastMinutes = (now - _lastProactiveTime).TotalMinutes;
            if (idleMinutes < profile.IdleThresholdMinutes)
            {
                Log.Debug($"[主动搭话] {status}｜未到独处阈值（{idleMinutes:F1}/{profile.IdleThresholdMinutes} 分钟）");
                return;
            }
            if (sinceLastMinutes < profile.CooldownMinutes)
            {
                Log.Debug($"[主动搭话] {status}｜冷却中（{sinceLastMinutes:F1}/{profile.CooldownMinutes} 分钟）");
                return;
            }

            if (Random.Shared.NextDouble() < profile.Probability)
            {
                Log.Debug($"[主动搭话] {status}｜触发：独处搭话（概率 {profile.Probability:P0} 命中）");
                Fire(ProactiveTriggerType.IdleChat, now);
            }
            else
            {
                Log.Debug($"[主动搭话] {status}｜条件满足，概率未命中（{profile.Probability:P0}），等下次心跳");
            }
        }

        /// <summary>心跳状态摘要：空闲时长、离开状态与距上次互动/主动的间隔，便于排查"为什么没搭话"。</summary>
        private string BuildStatusText(DateTime now, double idleSeconds)
        {
            var idleText = idleSeconds >= 60 ? $"{idleSeconds / 60:F0} 分钟" : $"{idleSeconds:F0} 秒";
            var sinceInteraction = (now - _lastPetInteractionTime).TotalMinutes;
            var sinceProactive = (now - _lastProactiveTime).TotalMinutes;
            return $"心跳：系统空闲 {idleText}｜{(_wasAway ? "离开中" : "在线")}｜" +
                   $"距上次互动 {sinceInteraction:F1} 分钟｜距上次主动 {sinceProactive:F1} 分钟";
        }

        /// <summary>
        /// 依据系统空闲时长更新"离开/回来"状态：首次达到离开阈值时记录离开时刻；
        /// 回到活动状态时返回 true，并开启欢迎判定宽限。
        /// </summary>
        private bool UpdateAwayState(DateTime now, double idleSeconds)
        {
            if (!_wasAway)
            {
                if (idleSeconds >= AWAY_IDLE_SECONDS)
                {
                    _wasAway = true;
                    _awaySince = now.AddSeconds(-idleSeconds);
                    _awayDetectedTime = now;
                    Log.Debug($"[主动搭话] 主人离开（系统空闲约 {idleSeconds / 60:F0} 分钟）");
                }
                return false;
            }

            if (idleSeconds >= RETURN_IDLE_SECONDS)
            {
                return false;
            }

            _wasAway = false;
            _pendingWelcomeUntil = now.AddMinutes(WELCOME_GRACE_MINUTES);
            return true;
        }

        /// <summary>欢迎回来判定：宽限期内、未被"先来互动"取消、且冷却已过时触发；各步结果均记录日志。</summary>
        private bool TryWelcomeBack(DateTime now, SettingsService settings)
        {
            if (!_pendingWelcomeUntil.HasValue)
            {
                return false;
            }

            // 回来之后主人已经先和桌宠互动过 → 取消本次欢迎
            if (_lastPetInteractionTime > _awayDetectedTime)
            {
                Log.Debug("[主动搭话] 欢迎判定取消：主人回来后又先互动过");
                _pendingWelcomeUntil = null;
                return false;
            }

            if (now > _pendingWelcomeUntil.Value)
            {
                Log.Debug($"[主动搭话] 欢迎判定失效：回来后的宽限期（{WELCOME_GRACE_MINUTES} 分钟）已过且未能开口");
                _pendingWelcomeUntil = null;
                return false;
            }

            if (!settings.ProactiveWelcomeBackEnabled)
            {
                Log.Debug("[主动搭话] 欢迎判定跳过：欢迎回来开关已关闭");
                return false;
            }
            if ((now - _lastWelcomeTime).TotalHours < WELCOME_COOLDOWN_HOURS)
            {
                var remainHours = WELCOME_COOLDOWN_HOURS - (now - _lastWelcomeTime).TotalHours;
                Log.Debug($"[主动搭话] 欢迎判定跳过：独立冷却中（还剩 {remainHours:F1} 小时）");
                return false;
            }
            if ((now - _lastProactiveTime).TotalMinutes < MIN_GAP_MINUTES)
            {
                Log.Debug($"[主动搭话] 欢迎判定跳过：距上次开口不足 {MIN_GAP_MINUTES} 分钟");
                return false;
            }

            _pendingWelcomeUntil = null;
            Fire(ProactiveTriggerType.WelcomeBack, now);
            return true;
        }

        /// <summary>
        /// 定时问候检查：进入早/晚窗口后随机挑一个目标时刻，到达且当天未问候时返回对应类型；否则返回 null。
        /// 不在窗口内时清除过期目标，窗口结束后当天不再问候。
        /// </summary>
        private ProactiveTriggerType? CheckGreeting(DateTime now)
        {
            var today = DateOnly.FromDateTime(now);
            var minuteOfDay = now.Hour * 60 + now.Minute;

            if (minuteOfDay >= MORNING_WINDOW_START_MINUTE && minuteOfDay < MORNING_WINDOW_END_MINUTE)
            {
                if (_morningGreetedDate != today)
                {
                    if (_morningTargetTime == null)
                    {
                        _morningTargetTime = PickGreetingTargetTime(now, MORNING_WINDOW_START_MINUTE, MORNING_WINDOW_END_MINUTE);
                        Log.Debug($"[主动搭话] 早上问候窗口内，随机目标时刻定为 {_morningTargetTime:HH:mm}");
                    }

                    if (now >= _morningTargetTime.Value)
                    {
                        return ProactiveTriggerType.MorningGreeting;
                    }
                }
            }
            else
            {
                _morningTargetTime = null;
            }

            if (minuteOfDay >= EVENING_WINDOW_START_MINUTE && minuteOfDay < EVENING_WINDOW_END_MINUTE)
            {
                if (_eveningGreetedDate != today)
                {
                    if (_eveningTargetTime == null)
                    {
                        _eveningTargetTime = PickGreetingTargetTime(now, EVENING_WINDOW_START_MINUTE, EVENING_WINDOW_END_MINUTE);
                        Log.Debug($"[主动搭话] 晚上问候窗口内，随机目标时刻定为 {_eveningTargetTime:HH:mm}");
                    }

                    if (now >= _eveningTargetTime.Value)
                    {
                        return ProactiveTriggerType.EveningGreeting;
                    }
                }
            }
            else
            {
                _eveningTargetTime = null;
            }

            return null;
        }

        /// <summary>在窗口前段随机挑一个目标时刻；进入窗口时已过目标（如中途启动）则立即允许触发。</summary>
        private static DateTime PickGreetingTargetTime(DateTime now, int windowStartMinute, int windowEndMinute)
        {
            var windowStart = now.Date.AddMinutes(windowStartMinute);
            var span = (windowEndMinute - windowStartMinute) * GREETING_TARGET_RANDOM_RATIO;
            var target = windowStart.AddMinutes(Random.Shared.NextDouble() * span);
            return target > now ? target : now;
        }

        /// <summary>触发一次主动搭话：记录时间、推进问候日期，并抛出事件交给主窗口生成台词。</summary>
        private void Fire(ProactiveTriggerType type, DateTime now)
        {
            _lastProactiveTime = now;

            switch (type)
            {
                case ProactiveTriggerType.WelcomeBack:
                    _lastWelcomeTime = now;
                    break;
                case ProactiveTriggerType.MorningGreeting:
                    _morningGreetedDate = DateOnly.FromDateTime(now);
                    _morningTargetTime = null;
                    break;
                case ProactiveTriggerType.EveningGreeting:
                    _eveningGreetedDate = DateOnly.FromDateTime(now);
                    _eveningTargetTime = null;
                    break;
            }

            var eventKey = GetEventKey(type);
            var contextNote = BuildContextNote(type, now);
            Log.Info($"[主动搭话] 触发 {type}｜{contextNote}");
            Triggered?.Invoke(this, new ProactiveTriggerEventArgs(type, eventKey, contextNote));
        }

        /// <summary>拼动态语境：把当前时间与间隔时长告诉模型，避免它凭空想象。</summary>
        private string BuildContextNote(ProactiveTriggerType type, DateTime now)
        {
            switch (type)
            {
                case ProactiveTriggerType.WelcomeBack:
                    var awayMinutes = Math.Max(1, (int)(now - _awaySince).TotalMinutes);
                    return $"现在是 {now:HH:mm}，主人离开了大约 {awayMinutes} 分钟，刚刚回到电脑前。";
                case ProactiveTriggerType.IdleChat:
                    var idleMinutes = Math.Max(1, (int)(now - _lastPetInteractionTime).TotalMinutes);
                    return $"现在是 {now:HH:mm}，主人已经 {idleMinutes} 分钟没有和你互动了。";
                default:
                    return $"现在是 {now:HH:mm}。";
            }
        }

        /// <summary>触发类型 → 角色包 reactions.json 的事件键。</summary>
        private static string GetEventKey(ProactiveTriggerType type) => type switch
        {
            ProactiveTriggerType.WelcomeBack => TouchReactionService.EventWelcomeBack,
            ProactiveTriggerType.MorningGreeting => TouchReactionService.EventMorningGreeting,
            ProactiveTriggerType.EveningGreeting => TouchReactionService.EventEveningGreeting,
            _ => TouchReactionService.EventIdleChat
        };

        /// <summary>安静时段判定：支持跨午夜；开始与结束小时相同表示不启用。</summary>
        private static bool IsQuietHour(DateTime now, SettingsService settings)
        {
            var start = settings.QuietHoursStart;
            var end = settings.QuietHoursEnd;
            if (start == end)
            {
                return false;
            }

            var hour = now.Hour;
            return start < end ? hour >= start && hour < end : hour >= start || hour < end;
        }

        /// <summary>频度档位 → 参数映射：低 60/120/15%，中 30/60/30%，高 15/25/50%。</summary>
        private static FrequencyProfile GetProfile(ProactiveFrequency frequency) => frequency switch
        {
            ProactiveFrequency.Low => new FrequencyProfile(60, 120, 0.15),
            ProactiveFrequency.High => new FrequencyProfile(15, 25, 0.50),
            _ => new FrequencyProfile(30, 60, 0.30)
        };

        // ── 系统空闲检测（Win32）───────────────────

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;

            public uint dwTime;
        }

        /// <summary>系统级空闲时长（秒）：距最近一次键盘/鼠标输入的时间；无输入信息时按 0 处理。</summary>
        private static double GetSystemIdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
            {
                return 0;
            }

            // 与 GetTickCount 同源（32 位毫秒，49.7 天回绕），用无符号减法天然处理回绕
            var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return idleMs / 1000.0;
        }
    }
}