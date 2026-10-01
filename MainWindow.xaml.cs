using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KfuPet.Models;
using KfuPet.Services;
using KfuPet.Services.Ipc;
using KfuPet.Services.Tools;

namespace KfuPet
{
    /// <summary>
    /// 透明无边框主窗口，承载角色渲染与鼠标交互。
    /// </summary>
    public partial class MainWindow : Window
    {
        // ── Win32 API ────────────────────────────────
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ── 窗口尺寸 ──────────────────────────────────

        /// <summary>窗口宽度（DIP）。随系统缩放等比放大，桌宠在各缩放设置下看起来一样大；改这里即可整体改窗口大小。</summary>
        private const double WINDOW_WIDTH = 480;

        /// <summary>窗口高度（DIP）。角色高 410 DIP（头顶点上 230、脚底点下 180），下方余量需容纳贴底的输入框。</summary>
        private const double WINDOW_HEIGHT = 600;

        // ── 长按拖动 ──────────────────────────────────
        private DispatcherTimer? _holdTimer;
        private bool _isDragging;
        private POINT _dragStartCursorPos;
        private double _windowStartLeft;
        private double _windowStartTop;

        private const int HOLD_DELAY_MS = 300;
        private const int DRAG_THRESHOLD = 5;
        private double _dpiScaleX = double.NaN;
        private double _dpiScaleY = double.NaN;

        // ── 触摸反应 ──────────────────────────────────

        /// <summary>触摸反应冷却时长（毫秒）：一次反应后 2 秒内再做触摸动作一律无效。</summary>
        private const int TOUCH_REACTION_COOLDOWN_MS = 2000;

        /// <summary>头部抚摸的单段横向位移下限（DIP）：达到该值才算一段滑动。</summary>
        private const double TOUCH_WIGGLE_STROKE_MIN = 20;

        /// <summary>判定滑动方向所需的最小位移（DIP），用于过滤鼠标抖动。</summary>
        private const double TOUCH_WIGGLE_DIRECTION_MIN = 4;

        /// <summary>触发抚摸反应所需的滑动段数：左一下 + 右一下。</summary>
        private const int TOUCH_WIGGLE_STROKES = 2;

        /// <summary>触摸反应台词服务：读取角色包 reactions.json，缺省时使用内置默认台词。</summary>
        private readonly TouchReactionService _touchReactions = new();

        /// <summary>上一次触摸反应的时间，用于冷却判定。</summary>
        private DateTime _lastTouchReactionTime = DateTime.MinValue;

        /// <summary>本次按下是否处于头部抚摸判定中（判定期间窗口不跟随移动）。</summary>
        private bool _touchGestureTracking;

        /// <summary>本次按下是否已触发过抚摸反应；触发后剩余动作一律无效。</summary>
        private bool _touchGestureConsumed;

        /// <summary>当前滑动段的起点 X（画布坐标 DIP）。</summary>
        private double _wiggleAnchorX;

        /// <summary>当前滑动段已到达的最远 X。</summary>
        private double _wiggleExtremeX;

        /// <summary>当前滑动段方向：1 向右、-1 向左、0 未定。</summary>
        private int _wiggleDirection;

        /// <summary>已完成的滑动段数。</summary>
        private int _wiggleStrokeCount;

        // ── 越界回正 ──────────────────────────────────

        /// <summary>窗口超出屏幕的比例达到该值（0~1）时触发回正。0.5 表示“一半越界才回正”；调小更严格，调大更宽容。</summary>
        private const double SNAP_BACK_OUTSIDE_RATIO = 0.4;

        /// <summary>下边判定时剔除的“输入框区域”高度（逻辑像素）：输入框贴在窗口最底部，不属于宠物本体，不参与下边越界判定。调大可把角色脚下的空白一并排除。</summary>
        private const double SNAP_BACK_BOTTOM_EXCLUDE = 64;

        /// <summary>回正滑动动画时长（毫秒），越大滑得越慢。</summary>
        private const double SNAP_BACK_DURATION_MS = 260;

        // ── 输入框显隐动画 ─────────────────────────────

        /// <summary>输入框淡入时长（毫秒）。</summary>
        private const double CHAT_INPUT_FADE_IN_MS = 200;

        /// <summary>输入框淡出时长（毫秒），略快于淡入，收起时更利落。</summary>
        private const double CHAT_INPUT_FADE_OUT_MS = 150;

        /// <summary>输入框淡入时向上浮动的距离（逻辑像素）。</summary>
        private const double CHAT_INPUT_SLIDE_IN = 8;

        private DispatcherTimer? _snapTimer;

        /// <summary>输入框是否处于“应显示”状态（含淡入淡出过程中），用于避免重复播放显隐动画。</summary>
        private bool _isChatInputVisible;

        /// <summary>输入框显隐动画的令牌：每次请求显隐都递增，过期的动画完成回调直接忽略，避免互相打断后状态错乱。</summary>
        private int _chatInputFadeToken;

        /// <summary>输入框是否因拖动被临时收起，用于松手后按最终位置恢复显示。</summary>
        private bool _chatInputHiddenByDrag;

        private Skeleton? _skeleton;

        /// <summary>本次启动加载附件配置的角色包目录；为空表示没有可回写的角色包。</summary>
        private string? _attachmentsPackageDir;

        internal SkeletonService SkeletonService { get; } = new SkeletonService();

        internal EmotionService EmotionService { get; } = new EmotionService();

        internal VisionService VisionService { get; } = new VisionService();

        /// <summary>全局日志服务（与应用其他部分共用同一实例）。</summary>
        internal LogService LogService => Log.Instance;

        internal DeveloperModeService DeveloperModeService { get; } = new DeveloperModeService();

        internal ModelConfigService ModelConfigService { get; } = new ModelConfigService();

        internal StopWordsService StopWordsService { get; } = new StopWordsService();

        internal CommandDispatcher CommandDispatcher { get; } = new CommandDispatcher();

        private NamedPipeServer? _pipeServer;

        private DispatcherTimer? _toolMonitorTimer;
        private bool _wasToolRunning;

        // ── AI 聊天 ──────────────────────────────────
        private readonly ChatService _chatService;
        private readonly MemorySystem _memorySystem;
        private readonly ToolRegistry _toolRegistry;

        /// <summary>记忆系统门面，供设置界面读取三级记忆状态。</summary>
        internal MemorySystem MemorySystem => _memorySystem;
        private bool _isSending;
        private bool _isHoveringPet;
        private bool _isHoveringInput;
        private DispatcherTimer? _inputHideTimer;
        private CancellationTokenSource? _bubbleCts;

        /// <summary>
        /// 开发者工具（KfuPet-Tool）进程是否正在运行。
        /// </summary>
        public bool IsToolRunning => DeveloperModeService.IsToolRunning();

        /// <summary>
        /// 开发者工具运行状态变化时触发，供设置界面同步显示。
        /// </summary>
        public event EventHandler? ToolRunningChanged;

        public MainWindow()
        {
            InitializeComponent();
            _chatService = new ChatService();
            _memorySystem = new MemorySystem(_chatService, LogService, StopWordsService);
            _toolRegistry = new ToolRegistry();
            _toolRegistry.Register(new WebSearchTool());
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dpi = GetDpiForWindow(hwnd);
            double dpiScale = dpi / 96.0;
            // 窗口与骨骼统一使用 DIP：DPI 越高物理像素越多，桌宠在各缩放设置下看起来一样大
            Width = WINDOW_WIDTH;
            Height = WINDOW_HEIGHT;
            RestoreOrCenterPosition();
            InitializeSkeleton();
            LoadCharacterAttachments();
            Log.Debug($"[窗口] 主窗口加载完成：{Width:F0}×{Height:F0}，DPI 缩放 {dpiScale:F2}");

            CommandDispatcher.RegisterService(SkeletonService);
            CommandDispatcher.RegisterService(EmotionService);
            CommandDispatcher.RegisterService(VisionService);

            _pipeServer = new NamedPipeServer(CommandDispatcher, Application.Current);

            // 命令管道由开发者模式开关控制，默认关闭；日志管道常开，由 App 在启动时拉起
            DeveloperModeService.EnabledChanged += OnDeveloperModeChanged;
            ApplyDeveloperMode();

            StartToolMonitor();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Log.Info("[窗口] 主窗口正在关闭，停止命名管道");
            SaveCharacterAttachments();
            _bubbleCts?.Cancel();
            _snapTimer?.Stop();
            _toolMonitorTimer?.Stop();
            _pipeServer?.Stop();
            _pipeServer?.Dispose();
        }

        /// <summary>
        /// 启动时从角色包加载部位图片附件（attachments.json），挂到已初始化的骨骼上。
        /// 没有找到带附件配置的角色包时跳过，角色只显示骨骼调试线框。
        /// </summary>
        private void LoadCharacterAttachments()
        {
            var packageDir = CharacterAttachmentService.FindPackageWithAttachments();
            if (packageDir == null)
            {
                Log.Info("[附件] 未找到带附件配置的角色包，跳过部位挂载");
                return;
            }

            _attachmentsPackageDir = packageDir;
            CharacterAttachmentService.Load(SkeletonService, packageDir);
            _touchReactions.Load(packageDir);
        }

        /// <summary>
        /// 关闭时把当前附件状态回写到角色包，固化在开发者工具里调好的位置。
        /// 未从角色包加载过附件时不写盘，避免清空已有配置。
        /// </summary>
        private void SaveCharacterAttachments()
        {
            if (_attachmentsPackageDir == null) return;
            CharacterAttachmentService.Save(SkeletonService, _attachmentsPackageDir);
        }

        private void OnDeveloperModeChanged(object? sender, EventArgs e)
        {
            ApplyDeveloperMode();
            ToolRunningChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 根据开发者模式开关状态启动或停止供开发者工具连接的命名管道。
        /// 只管命令管道；日志管道常开，不随该开关变化。
        /// </summary>
        private void ApplyDeveloperMode()
        {
            if (DeveloperModeService.IsEnabled)
            {
                _pipeServer?.Start();
                Log.Info("[开发者模式] 已开启，命名管道开始监听");
            }
            else
            {
                _pipeServer?.Stop();
                Log.Info("[开发者模式] 已关闭，命名管道已停止");
            }
        }

        /// <summary>
        /// 定时检测开发者工具进程是否运行，状态变化时通知设置界面。
        /// </summary>
        private void StartToolMonitor()
        {
            _wasToolRunning = DeveloperModeService.IsToolRunning();

            _toolMonitorTimer = new DispatcherTimer();
            _toolMonitorTimer.Interval = TimeSpan.FromSeconds(2);
            _toolMonitorTimer.Tick += (s, e) =>
            {
                var running = DeveloperModeService.IsToolRunning();
                if (running != _wasToolRunning)
                {
                    _wasToolRunning = running;
                    Log.Info($"[IPC] 开发者工具{(running ? "已启动" : "已退出")}");
                    ToolRunningChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _toolMonitorTimer.Start();
        }

        private void InitializeSkeleton()
        {
            _skeleton = new Skeleton();

            // ==================== 根骨骼 ====================
            // 根骨骼位于窗口中心，窗口尺寸变化时自动跟随，避免角色偏位
            _skeleton.AddBone(new Bone
            {
                Id = "root",
                Name = "Root",
                ParentId = null,
                LocalPosition = new Point(WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2)
            });

            // body 兼作躯干锚点，接管原 neck 所在的位置
            _skeleton.AddBone(new Bone
            {
                Id = "body",
                Name = "Body",
                ParentId = "root",
                LocalPosition = new Point(0, -130)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "head",
                Name = "Head",
                ParentId = "body",
                LocalPosition = new Point(0, -60)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_left_upper",
                Name = "LeftArmUpper",
                ParentId = "body",
                LocalPosition = new Point(-80, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_left_lower",
                Name = "LeftArmLower",
                ParentId = "arm_left_upper",
                LocalPosition = new Point(-100, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_right_upper",
                Name = "RightArmUpper",
                ParentId = "body",
                LocalPosition = new Point(80, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_right_lower",
                Name = "RightArmLower",
                ParentId = "arm_right_upper",
                LocalPosition = new Point(100, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_left_upper",
                Name = "LeftLegUpper",
                ParentId = "root",
                LocalPosition = new Point(-40, 80)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_left_lower",
                Name = "LeftLegLower",
                ParentId = "leg_left_upper",
                LocalPosition = new Point(0, 100)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_right_upper",
                Name = "RightLegUpper",
                ParentId = "root",
                LocalPosition = new Point(40, 80)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_right_lower",
                Name = "RightLegLower",
                ParentId = "leg_right_upper",
                LocalPosition = new Point(0, 100)
            });

            // ==================== 更新变换 ====================
            _skeleton.UpdateWorldTransforms();  // 计算所有骨骼的世界坐标
            CharacterCanvas.Skeleton = _skeleton;  // 将骨骼绑定到渲染画布

            SkeletonService.BindSkeleton(_skeleton);
            SkeletonService.SkeletonChanged += OnSkeletonServiceChanged;
            SkeletonService.DebugSkeletonChanged += OnDebugSkeletonChanged;

            // 应用上次保存的调试线框开关（状态变化事件不会在启动时触发）
            CharacterCanvas.ShowDebugBones = SkeletonService.ShowDebugSkeleton;

            Log.Info($"[骨骼] 骨骼树初始化完成，共 {_skeleton.Bones.Count} 根骨骼");
        }

        private void OnSkeletonServiceChanged(object? sender, EventArgs e)
        {
            if (_skeleton != null)
            {
                CharacterCanvas.Render();
            }
        }

        private void OnDebugSkeletonChanged(object? sender, EventArgs e)
        {
            CharacterCanvas.ShowDebugBones = SkeletonService.ShowDebugSkeleton;
        }

        private void CenterWindow()
        {
            var screenWidth = System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - Width) / 2;
            Top = (screenHeight - Height) / 2;
        }

        /// <summary>
        /// 恢复上次记录的桌宠位置；没有记录时居中显示。
        /// 位置与窗口宽高同为逻辑像素（DIP），坐标系一致。
        /// </summary>
        private void RestoreOrCenterPosition()
        {
            var settings = SettingsService.Instance;
            if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
            {
                Left = settings.WindowLeft.Value;
                Top = settings.WindowTop.Value;
            }
            else
            {
                CenterWindow();
            }
        }

        /// <summary>
        /// 播放主窗口淡入动画。
        /// </summary>
        public void PlayFadeInAnimation()
        {
            var storyboard = (Storyboard)RootGrid.Resources["FadeInStoryboard"];
            storyboard.Begin();
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 点击落在输入框区域时不触发长按拖动，交给输入框处理
            if (ChatInputPanel.IsMouseOver)
            {
                return;
            }

            // 双击部位：触发该部位的触摸反应，本次按下不再参与拖动
            if (e.ClickCount == 2)
            {
                var doubleClickBoneId = CharacterCanvas.HitTestAttachmentBoneId(e.GetPosition(CharacterCanvas));
                if (doubleClickBoneId != null)
                {
                    TriggerTouchReaction(doubleClickBoneId);
                    return;
                }
            }

            // 新的拖动会接管窗口位置，先停止尚未播完的回正滑动
            _snapTimer?.Stop();
            _snapTimer = null;

            GetCursorPos(out _dragStartCursorPos);
            _windowStartLeft = Left;
            _windowStartTop = Top;

            // 按下落在头部时进入抚摸判定：头部只响应抚摸/双击，不参与拖动窗口
            var pressPoint = e.GetPosition(CharacterCanvas);
            ResetTouchGesture();
            var pressOnHead = TouchReactionService.ResolvePartKey(CharacterCanvas.HitTestAttachmentBoneId(pressPoint))
                              == TouchReactionService.PartHead;
            if (pressOnHead)
            {
                _touchGestureTracking = true;
                _wiggleAnchorX = pressPoint.X;
                _wiggleExtremeX = pressPoint.X;
            }
            else
            {
                // 非头部部位才启动长按拖动计时
                _holdTimer = new DispatcherTimer();
                _holdTimer.Interval = TimeSpan.FromMilliseconds(HOLD_DELAY_MS);
                _holdTimer.Tick += (s, args) =>
                {
                    _holdTimer?.Stop();
                    StartDrag();
                };
                _holdTimer.Start();
            }

            Mouse.Capture(RootGrid);
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            // 拖动时窗口整体跟随光标，光标相对窗口的位置不变，命中测试结果也不会变；
            // 跳过它可以省下每帧的逐像素检测，避免拖动掉帧。
            if (!_isDragging)
            {
                var canvasPoint = e.GetPosition(CharacterCanvas);
                UpdatePetHover(canvasPoint);

                // 头部抚摸判定：按住头部来回滑动期间窗口保持不动，滑动完成触发反应
                if (_touchGestureTracking)
                {
                    UpdateHeadWiggle(canvasPoint);
                    return;
                }
            }

            if (_holdTimer == null && !_isDragging) return;

            if (!_isDragging)
            {
                GetCursorPos(out POINT currentPos);
                int dx = currentPos.X - _dragStartCursorPos.X;
                int dy = currentPos.Y - _dragStartCursorPos.Y;

                if (dx * dx + dy * dy <= DRAG_THRESHOLD * DRAG_THRESHOLD)
                    return;

                // 超过阈值，立即进入拖拽（不重置起始参考点）
                _holdTimer?.Stop();
                _holdTimer = null;
                _isDragging = true;
                // 继续往下执行，立即更新窗口位置
            }

            // 窗口即将移动，输入框跟随会因位置不同步而抖动，先收起
            HideChatInputForDrag();

            GetCursorPos(out POINT pos);
            var (sx, sy) = GetDpiScale();
            Left = _windowStartLeft + (pos.X - _dragStartCursorPos.X) * sx;
            Top = _windowStartTop + (pos.Y - _dragStartCursorPos.Y) * sy;
        }

        private void StartDrag()
        {
            // 长按后转入拖动：以当前位置重新锚定，窗口不会跳到光标累计位移处
            GetCursorPos(out _dragStartCursorPos);
            _windowStartLeft = Left;
            _windowStartTop = Top;

            _isDragging = true;
        }

        private (double scaleX, double scaleY) GetDpiScale()
        {
            if (!double.IsNaN(_dpiScaleX))
                return (_dpiScaleX, _dpiScaleY);

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;
            }
            else
            {
                _dpiScaleX = 1.0;
                _dpiScaleY = 1.0;
            }
            return (_dpiScaleX, _dpiScaleY);
        }

        /// <summary>
        /// 判定窗口超出工作区的部分是否达到 SNAP_BACK_OUTSIDE_RATIO（下边判定会剔除贴底的输入框区域）。
        /// 若达到，返回贴齐对应屏幕边缘（左/右/上/下）的回正目标位置，否则返回 null。
        /// </summary>
        private (double Left, double Top)? GetSnapBackTarget()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return null;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
                return null;

            // 工作区为物理像素，按窗口所在屏幕的缩放换算为逻辑像素，与拖动使用同一坐标系
            var (scaleX, scaleY) = GetDpiScale();
            double workLeft = info.rcWork.Left * scaleX;
            double workTop = info.rcWork.Top * scaleY;
            double workRight = info.rcWork.Right * scaleX;
            double workBottom = info.rcWork.Bottom * scaleY;

            // 窗口超出工作区边缘的比例达到 SNAP_BACK_OUTSIDE_RATIO 即视为越界
            double marginX = Width * SNAP_BACK_OUTSIDE_RATIO;
            double marginY = Height * SNAP_BACK_OUTSIDE_RATIO;

            // 下边判定剔除贴底的输入框区域，只按上层宠物内容高度计算越界比例
            double bottomHeight = Height - SNAP_BACK_BOTTOM_EXCLUDE;
            double bottomMarginY = bottomHeight * SNAP_BACK_OUTSIDE_RATIO;

            double left = Left;
            double top = Top;
            bool offScreen = false;

            if (workLeft - Left >= marginX)
            {
                left = workLeft;
                offScreen = true;
            }
            else if (Left + Width - workRight >= marginX)
            {
                left = workRight - Width;
                offScreen = true;
            }

            if (workTop - Top >= marginY)
            {
                top = workTop;
                offScreen = true;
            }
            else if (Top + bottomHeight - workBottom >= bottomMarginY)
            {
                top = workBottom - Height;
                offScreen = true;
            }

            return offScreen ? (left, top) : null;
        }

        /// <summary>
        /// 用缓出动画把窗口平滑滑动到目标位置。
        /// </summary>
        /// <param name="onCompleted">滑动结束后的回调，用于在窗口静止后再恢复输入框等依赖位置的 UI。</param>
        private void SlideWindowTo(double targetLeft, double targetTop, Action? onCompleted = null)
        {
            _snapTimer?.Stop();

            double startLeft = Left;
            double startTop = Top;
            var stopwatch = Stopwatch.StartNew();

            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            timer.Tick += (s, e) =>
            {
                double progress = Math.Min(stopwatch.Elapsed.TotalMilliseconds / SNAP_BACK_DURATION_MS, 1.0);
                double eased = 1 - Math.Pow(1 - progress, 3); // 缓出：快起慢停

                Left = startLeft + (targetLeft - startLeft) * eased;
                Top = startTop + (targetTop - startTop) * eased;

                if (progress >= 1)
                {
                    timer.Stop();
                    _snapTimer = null;
                    onCompleted?.Invoke();
                }
            };

            _snapTimer = timer;
            timer.Start();
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _holdTimer?.Stop();
            _holdTimer = null;

            if (_isDragging)
            {
                _isDragging = false;

                var snapTarget = GetSnapBackTarget();
                if (snapTarget.HasValue)
                {
                    // 越界达到阈值：回正贴边并说话，回正后的位置作为下次启动的恢复位置
                    var (targetLeft, targetTop) = snapTarget.Value;
                    SettingsService.Instance.SetWindowPosition(targetLeft, targetTop);
                    SlideWindowTo(targetLeft, targetTop, RestoreChatInputAfterDrag);
                    ShowBubbleBatches(new List<string> { "呜哇！差点掉出屏幕啦，我先靠边站好～" });
                    Log.Debug($"[窗口] 拖动越界，已回正到 ({targetLeft:F0}, {targetTop:F0})");
                }
                else
                {
                    // 拖动结束后记录桌宠位置，下次启动恢复
                    SettingsService.Instance.SetWindowPosition(Left, Top);
                    RestoreChatInputAfterDrag();
                }
            }

            ResetTouchGesture();
            Mouse.Capture(null);
        }

        // ── 触摸反应：双击与头部抚摸 ─────────────────

        /// <summary>
        /// 清空本次按下的抚摸判定状态（按下与松开时调用）。
        /// </summary>
        private void ResetTouchGesture()
        {
            _touchGestureTracking = false;
            _touchGestureConsumed = false;
            _wiggleDirection = 0;
            _wiggleStrokeCount = 0;
            _wiggleAnchorX = 0;
            _wiggleExtremeX = 0;
        }

        /// <summary>
        /// 推进头部抚摸判定：横向来回滑动（左一下 + 右一下，每段至少 TOUCH_WIGGLE_STROKE_MIN DIP）
        /// 完成时触发触摸反应。头部不参与拖动窗口，整个判定期间窗口保持不动。
        /// </summary>
        private void UpdateHeadWiggle(Point canvasPoint)
        {
            if (_touchGestureConsumed) return;

            // 方向未定：从按下点横向移动一小段后确定滑动方向
            if (_wiggleDirection == 0)
            {
                var offset = canvasPoint.X - _wiggleAnchorX;
                if (Math.Abs(offset) < TOUCH_WIGGLE_DIRECTION_MIN) return;

                _wiggleDirection = offset > 0 ? 1 : -1;
                _wiggleExtremeX = canvasPoint.X;
                return;
            }

            // 继续沿当前方向滑动：本段最远点继续外扩
            if ((canvasPoint.X - _wiggleExtremeX) * _wiggleDirection >= 0)
            {
                _wiggleExtremeX = canvasPoint.X;
                return;
            }

            // 方向掉头：上一段位移达到下限才计一段，否则视为手抖
            if (Math.Abs(_wiggleExtremeX - _wiggleAnchorX) >= TOUCH_WIGGLE_STROKE_MIN)
            {
                _wiggleStrokeCount++;
                _wiggleAnchorX = _wiggleExtremeX;   // 上一段的终点成为新一段的起点
                _wiggleDirection = -_wiggleDirection;
                _wiggleExtremeX = canvasPoint.X;

                if (_wiggleStrokeCount >= TOUCH_WIGGLE_STROKES)
                {
                    _touchGestureConsumed = true;
                    TriggerTouchReaction("head");
                }
                return;
            }

            // 上半段过短（手抖）：不计段，直接把当前方向当作新的滑动方向，起点保持不变
            _wiggleDirection = canvasPoint.X >= _wiggleAnchorX ? 1 : -1;
            _wiggleExtremeX = canvasPoint.X;
        }

        /// <summary>
        /// 触发一次触摸反应：说出该部位对应的台词。冷却期间（2 秒）的触摸动作一律无效。
        /// </summary>
        private void TriggerTouchReaction(string boneId)
        {
            var partKey = TouchReactionService.ResolvePartKey(boneId);
            if (partKey == null) return;

            var elapsedMs = (DateTime.UtcNow - _lastTouchReactionTime).TotalMilliseconds;
            if (elapsedMs < TOUCH_REACTION_COOLDOWN_MS)
            {
                Log.Debug($"[触摸] 冷却中（{elapsedMs:F0} ms），本次触摸无效：{boneId}");
                return;
            }

            var line = _touchReactions.PickLine(partKey);
            if (line == null) return;

            _lastTouchReactionTime = DateTime.UtcNow;
            ShowBubbleBatches(new List<string> { line });
            Log.Info($"[触摸] {boneId} → {partKey}：{line}");
        }

        // ── AI 聊天：悬停输入框 ─────────────────────

        /// <summary>
        /// 鼠标移动时检测是否悬停在角色不透明区域，控制输入框显隐。
        /// </summary>
        private void UpdatePetHover(Point canvasPoint)
        {
            var hovering = CharacterCanvas.HitTestOpaque(canvasPoint);
            if (hovering == _isHoveringPet) return;

            _isHoveringPet = hovering;
            if (hovering)
            {
                ShowChatInput();
            }
            else
            {
                ScheduleChatInputHide();
            }
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHoveringPet = false;
            ScheduleChatInputHide();
        }

        private void ChatInputPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            _isHoveringInput = true;
            _inputHideTimer?.Stop();
        }

        private void ChatInputPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHoveringInput = false;
            ScheduleChatInputHide();
        }

        /// <summary>
        /// 计算输入框需要向上偏移的距离（逻辑像素，向上为负）。返回 0 表示无需偏移。
        /// 用窗口在屏幕上的真实矩形与显示器工作区（都是设备像素、整数）做整数对齐，
        /// 若改用 Window.Left/Top 会因请求值与系统实际取整后的位置存在亚像素差而产生抖动。
        /// </summary>
        private double GetChatInputBottomShift()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(hwnd, out RECT windowRect))
                return 0;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return 0;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
                return 0;

            // scaleY 为“逻辑像素 → 设备像素”的换算系数
            var (_, scaleY) = GetDpiScale();
            double toDevice = 1.0 / scaleY;

            // 输入框底边在屏幕上的位置 = 窗口底边 - 下边距，允许的最大上移量即它与工作区底边的差
            double marginBottomDevice = ChatInputPanel.Margin.Bottom * toDevice;
            double maxShiftDevice = info.rcWork.Bottom - windowRect.Bottom + marginBottomDevice;

            if (maxShiftDevice >= 0)
                return 0;

            // 向下取整到整设备像素：既保证输入框完整可见，又让偏移量是整数、不再逐帧抖动
            return Math.Floor(maxShiftDevice) * scaleY;
        }

        /// <summary>
        /// 拖动开始时收起输入框（渐隐）。
        /// 拖动过程中窗口位置逐帧变化，输入框若跟随移动无法与窗口位置严格同步（两者不在同一时钟上），
        /// 会表现为明显抖动；因此先收起，等松手、窗口静止后再按最终位置重新弹出。
        /// </summary>
        private void HideChatInputForDrag()
        {
            if (!_isChatInputVisible)
                return;

            _inputHideTimer?.Stop();
            _isHoveringInput = false;
            _chatInputHiddenByDrag = true;

            FadeOutChatInput();
        }

        /// <summary>
        /// 拖动结束后恢复输入框：此时窗口已静止，重新弹出不会抖动。
        /// 回正滑动期间不恢复，由滑动结束回调触发。
        /// </summary>
        private void RestoreChatInputAfterDrag()
        {
            if (!_chatInputHiddenByDrag)
                return;

            _chatInputHiddenByDrag = false;

            // 拖动中光标始终停在角色上，松手后仍处于悬停状态才恢复
            if (!_isHoveringPet && !_isHoveringInput)
                return;

            ShowChatInput();
        }

        /// <summary>
        /// 显示输入框：淡入（透明度渐显）+ 轻微上浮，并把焦点交给文本框。
        /// 窗口下方越界时输入框会整体上移，避免被屏幕边缘挡住。
        /// </summary>
        private void ShowChatInput()
        {
            _inputHideTimer?.Stop();

            // 回正滑动尚未结束：窗口位置还在变化，此时显示会抖动，等滑动结束再显示
            if (_snapTimer != null) return;

            // 已在显示或正在淡入，无需重复播放
            if (_isChatInputVisible) return;

            _isChatInputVisible = true;
            ++_chatInputFadeToken;

            ChatInputPanel.Visibility = Visibility.Visible;

            // 窗口下边缘越界时，把输入框整体上移到可见区域内
            double shift = GetChatInputBottomShift();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 不指定起始值：从当前透明度接着渐变，淡出途中被重新唤出时不会闪跳
            ChatInputPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(CHAT_INPUT_FADE_IN_MS))
                {
                    EasingFunction = ease
                });

            if (ChatInputPanel.RenderTransform is TranslateTransform translate)
            {
                // 在上浮动画的基准位置上叠加越界偏移，动画结束后停在偏移位置
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(shift + CHAT_INPUT_SLIDE_IN, shift,
                        TimeSpan.FromMilliseconds(CHAT_INPUT_FADE_IN_MS))
                    {
                        EasingFunction = ease
                    });
            }
        }

        /// <summary>
        /// 淡出并收起输入框（透明度渐隐），拖动收起与悬停超时收起共用。
        /// 同一时刻只保留一个淡出：淡出途中若被要求重新显示，其完成回调会因令牌过期而放弃收起。
        /// </summary>
        private void FadeOutChatInput()
        {
            if (!_isChatInputVisible)
                return;

            _isChatInputVisible = false;
            int token = ++_chatInputFadeToken;

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(CHAT_INPUT_FADE_OUT_MS))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (token != _chatInputFadeToken) return;

                ChatInputPanel.Visibility = Visibility.Collapsed;
            };
            ChatInputPanel.BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// 延迟收起输入框：给鼠标从角色移到输入框留出缓冲时间。
        /// </summary>
        private void ScheduleChatInputHide()
        {
            if (!_isChatInputVisible) return;

            _inputHideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _inputHideTimer.Tick -= InputHideTimer_Tick;
            _inputHideTimer.Tick += InputHideTimer_Tick;
            _inputHideTimer.Stop();
            _inputHideTimer.Start();
        }

        private void InputHideTimer_Tick(object? sender, EventArgs e)
        {
            _inputHideTimer?.Stop();
            if (_isHoveringPet || _isHoveringInput || ChatInputBox.IsKeyboardFocused) return;

            FadeOutChatInput();
        }

        // ── AI 聊天：发送与气泡 ─────────────────────

        private void ChatInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ChatInputHint.Visibility = string.IsNullOrEmpty(ChatInputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SendChatAsync();
            }
        }

        private void ChatSendButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SendChatAsync();
        }

        /// <summary>
        /// 发送用户输入：调用当前启用的模型，回复通过气泡分批显示。
        /// </summary>
        private async Task SendChatAsync()
        {
            if (_isSending) return;

            var text = ChatInputBox.Text.Trim();
            if (text.Length == 0) return;

            var model = ModelConfigService.Models.FirstOrDefault(m => m.IsActive);
            if (model == null)
            {
                Log.Warning("[对话] 尚未配置可用模型，已提示用户先添加模型");
                ShowBubbleBatches(new List<string> { "主人还没有配置模型哦，去设置里添加一个再来找我吧～" });
                return;
            }

            _isSending = true;
            ChatInputBox.Clear();
            ShowBubbleBatches(new List<string> { "唔……" });
            Log.Info($"[对话] 用户发送消息 {text.Length} 字，使用模型 {model.ModelId}（{model.ModelName}）");
            try
            {
                var systemPrompt = await _memorySystem.BuildContextAsync(model, text);
                var history = _memorySystem.GetShortTermMessages();
                var reply = await _chatService.SendWithToolsAsync(
                    model, systemPrompt, history, text,
                    _toolRegistry.GetDefinitions(), _toolRegistry.ExecuteAsync);
                ShowBubbleBatches(SplitIntoBatches(reply));
                Log.Debug($"[对话] 回复已显示：{reply.Length} 字");

                // 记录一轮对话到记忆系统（短期 + 溢出归档 + 后台分析）
                _memorySystem.AddTurn(model, text, reply);
            }
            catch (Exception ex)
            {
                Log.Error($"[对话] 请求失败：{ex.Message}");
                ShowBubbleBatches(new List<string> { $"连接失败了……{ex.Message}" });
            }
            finally
            {
                _isSending = false;
            }
        }

        /// <summary>
        /// 把长回复按句子边界切成多批，每批不超过 60 字。
        /// </summary>
        private static List<string> SplitIntoBatches(string text)
        {
            const int maxBatchLength = 60;
            var batches = new List<string>();
            var current = new StringBuilder();

            foreach (var ch in text)
            {
                current.Append(ch);
                // 句子结束标点处切分；超长时强制切分
                if ("。！？!?\n".Contains(ch) || current.Length >= maxBatchLength)
                {
                    var batch = current.ToString().Trim();
                    if (batch.Length > 0)
                    {
                        batches.Add(batch);
                    }
                    current.Clear();
                }
            }

            var tail = current.ToString().Trim();
            if (tail.Length > 0)
            {
                batches.Add(tail);
            }

            return batches.Count > 0 ? batches : new List<string> { text };
        }

        /// <summary>
        /// 气泡分批显示：每批淡入展示一段时间，播完后自动淡出。
        /// 新的显示请求会打断上一轮的播放。
        /// </summary>
        private void ShowBubbleBatches(List<string> batches)
        {
            _bubbleCts?.Cancel();
            var cts = new CancellationTokenSource();
            _bubbleCts = cts;

            _ = RunBubbleBatchesAsync(batches, cts.Token);
        }

        private async Task RunBubbleBatchesAsync(List<string> batches, CancellationToken token)
        {
            try
            {
                for (var i = 0; i < batches.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    ChatBubbleText.Text = batches[i];
                    ChatBubble.Visibility = Visibility.Visible;

                    // 每批淡入 + 轻微上浮
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    ChatBubble.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                        {
                            EasingFunction = ease
                        });
                    if (ChatBubble.RenderTransform is TranslateTransform translateIn)
                    {
                        translateIn.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220))
                            {
                                EasingFunction = ease
                            });
                    }

                    // 每批停留时长随字数增加，保证可读
                    var dwellMs = Math.Clamp(1200 + batches[i].Length * 90, 1500, 6000);
                    await Task.Delay(dwellMs, token);

                    if (i < batches.Count - 1)
                    {
                        await FadeBubbleAsync(0, token);
                    }
                }

                await Task.Delay(1200, token);
                await FadeBubbleAsync(0, token);
                ChatBubble.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                // 被新一轮显示打断，直接退出
            }
        }

        private Task FadeBubbleAsync(double to, CancellationToken token)
        {
            var tcs = new TaskCompletionSource();
            token.Register(() => tcs.TrySetCanceled());

            var fade = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150));
            fade.Completed += (s, args) => tcs.TrySetResult();
            ChatBubble.BeginAnimation(OpacityProperty, fade);
            return tcs.Task;
        }
    }
}
