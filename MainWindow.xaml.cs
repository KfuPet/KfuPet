using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KfuPet.Models;
using KfuPet.Services;
using KfuPet.Services.Ipc;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

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

        private Skeleton? _skeleton;

        internal SkeletonService SkeletonService { get; } = new SkeletonService();

        internal MemoryService MemoryService { get; } = new MemoryService();

        internal EmotionService EmotionService { get; } = new EmotionService();

        internal VisionService VisionService { get; } = new VisionService();

        internal LogService LogService { get; } = new LogService();

        internal DeveloperModeService DeveloperModeService { get; } = new DeveloperModeService();

        internal ModelConfigService ModelConfigService { get; } = new ModelConfigService();

        internal StopWordsService StopWordsService { get; } = new StopWordsService();

        internal CommandDispatcher CommandDispatcher { get; } = new CommandDispatcher();

        private NamedPipeServer? _pipeServer;

        private LogPipeServer? _logPipeServer;

        private DispatcherTimer? _toolMonitorTimer;
        private bool _wasToolRunning;

        // ── AI 聊天 ──────────────────────────────────
        private readonly ChatService _chatService;
        private readonly MemorySystem _memorySystem;
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
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dpi = GetDpiForWindow(hwnd);
            double dpiScale = dpi / 96.0;
            Width = 512 / dpiScale;
            Height = 768 / dpiScale;
            CenterWindow();
            InitializeSkeleton(dpiScale);

            CommandDispatcher.RegisterService(SkeletonService);
            CommandDispatcher.RegisterService(MemoryService);
            CommandDispatcher.RegisterService(EmotionService);
            CommandDispatcher.RegisterService(VisionService);

            _pipeServer = new NamedPipeServer(CommandDispatcher, Application.Current);
            _logPipeServer = new LogPipeServer(LogService);

            // 管道由开发者模式开关控制，默认关闭
            DeveloperModeService.EnabledChanged += OnDeveloperModeChanged;
            ApplyDeveloperMode();

            StartToolMonitor();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _bubbleCts?.Cancel();
            _toolMonitorTimer?.Stop();
            _pipeServer?.Stop();
            _pipeServer?.Dispose();
            _logPipeServer?.Stop();
            _logPipeServer?.Dispose();
        }

        private void OnDeveloperModeChanged(object? sender, EventArgs e)
        {
            ApplyDeveloperMode();
            ToolRunningChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 根据开发者模式开关状态启动或停止供开发者工具连接的命名管道。
        /// </summary>
        private void ApplyDeveloperMode()
        {
            if (DeveloperModeService.IsEnabled)
            {
                _pipeServer?.Start();
                _logPipeServer?.Start();
                LogService.Info("开发者模式已开启");
            }
            else
            {
                _pipeServer?.Stop();
                _logPipeServer?.Stop();
                LogService.Info("开发者模式已关闭");
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
                    LogService.Info($"开发者工具{(running ? "已启动" : "已退出")}");
                    ToolRunningChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _toolMonitorTimer.Start();
        }

        private void InitializeSkeleton(double dpiScale)
        {
            _skeleton = new Skeleton();

            // ==================== 根骨骼 ====================
            _skeleton.AddBone(new Bone
            {
                Id = "root",
                Name = "Root",
                ParentId = null,
                LocalPosition = new Point(256 / dpiScale, 384 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "body",
                Name = "Body",
                ParentId = "root",
                LocalPosition = new Point(0, -100 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "neck",
                Name = "Neck",
                ParentId = "body",
                LocalPosition = new Point(0, -80 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "head",
                Name = "Head",
                ParentId = "neck",
                LocalPosition = new Point(0, -50 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_left_upper",
                Name = "LeftArmUpper",
                ParentId = "body",
                LocalPosition = new Point(-80 / dpiScale, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_left_lower",
                Name = "LeftArmLower",
                ParentId = "arm_left_upper",
                LocalPosition = new Point(-100 / dpiScale, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_right_upper",
                Name = "RightArmUpper",
                ParentId = "body",
                LocalPosition = new Point(80 / dpiScale, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "arm_right_lower",
                Name = "RightArmLower",
                ParentId = "arm_right_upper",
                LocalPosition = new Point(100 / dpiScale, 0)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_left_upper",
                Name = "LeftLegUpper",
                ParentId = "root",
                LocalPosition = new Point(-40 / dpiScale, 80 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_left_lower",
                Name = "LeftLegLower",
                ParentId = "leg_left_upper",
                LocalPosition = new Point(0, 100 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_right_upper",
                Name = "RightLegUpper",
                ParentId = "root",
                LocalPosition = new Point(40 / dpiScale, 80 / dpiScale)
            });

            _skeleton.AddBone(new Bone
            {
                Id = "leg_right_lower",
                Name = "RightLegLower",
                ParentId = "leg_right_upper",
                LocalPosition = new Point(0, 100 / dpiScale)
            });

            // ==================== 更新变换 ====================
            _skeleton.UpdateWorldTransforms();  // 计算所有骨骼的世界坐标
            CharacterCanvas.Skeleton = _skeleton;  // 将骨骼绑定到渲染画布

            SkeletonService.BindSkeleton(_skeleton);
            SkeletonService.SkeletonChanged += OnSkeletonServiceChanged;
            SkeletonService.DebugSkeletonChanged += OnDebugSkeletonChanged;
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

            GetCursorPos(out _dragStartCursorPos);
            _windowStartLeft = Left;
            _windowStartTop = Top;

            _holdTimer = new DispatcherTimer();
            _holdTimer.Interval = TimeSpan.FromMilliseconds(HOLD_DELAY_MS);
            _holdTimer.Tick += (s, args) =>
            {
                _holdTimer?.Stop();
                StartDrag();
            };
            _holdTimer.Start();

            Mouse.Capture(RootGrid);
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            UpdatePetHover(e.GetPosition(CharacterCanvas));

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

            GetCursorPos(out POINT pos);
            var (sx, sy) = GetDpiScale();
            Left = _windowStartLeft + (pos.X - _dragStartCursorPos.X) * sx;
            Top = _windowStartTop + (pos.Y - _dragStartCursorPos.Y) * sy;
        }

        private void StartDrag()
        {
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

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _holdTimer?.Stop();
            _holdTimer = null;

            if (_isDragging)
            {
                _isDragging = false;
            }

            Mouse.Capture(null);
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
        /// 显示输入框（淡入 + 轻微上浮），并把焦点交给文本框。
        /// </summary>
        private void ShowChatInput()
        {
            _inputHideTimer?.Stop();
            if (ChatInputPanel.Visibility == Visibility.Visible) return;

            ChatInputPanel.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ChatInputPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

            if (ChatInputPanel.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            }
        }

        /// <summary>
        /// 延迟收起输入框：给鼠标从角色移到输入框留出缓冲时间。
        /// </summary>
        private void ScheduleChatInputHide()
        {
            if (ChatInputPanel.Visibility != Visibility.Visible) return;

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

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (s, args) =>
            {
                if (!_isHoveringPet && !_isHoveringInput && !ChatInputBox.IsKeyboardFocused)
                {
                    ChatInputPanel.Visibility = Visibility.Collapsed;
                }
            };
            ChatInputPanel.BeginAnimation(OpacityProperty, fade);
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
                ShowBubbleBatches(new List<string> { "主人还没有配置模型哦，去设置里添加一个再来找我吧～" });
                return;
            }

            _isSending = true;
            ChatInputBox.Clear();
            ShowBubbleBatches(new List<string> { "唔……" });
            try
            {
                var systemPrompt = await _memorySystem.BuildContextAsync(model, text);
                var history = _memorySystem.GetShortTermMessages();
                var reply = await _chatService.SendAsync(model, systemPrompt, history, text);
                ShowBubbleBatches(SplitIntoBatches(reply));

                // 记录一轮对话到记忆系统（短期 + 溢出归档 + 后台分析）
                _memorySystem.AddTurn(model, text, reply);
            }
            catch (Exception ex)
            {
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
