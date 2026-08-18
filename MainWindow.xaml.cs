using System;
using System.Runtime.InteropServices;
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

        // 此代码只做演示作用，后期会进行修改删除
        private Skeleton? _skeleton;

        internal SkeletonService SkeletonService { get; } = new SkeletonService();

        internal MemoryService MemoryService { get; } = new MemoryService();

        internal EmotionService EmotionService { get; } = new EmotionService();

        internal VisionService VisionService { get; } = new VisionService();

        internal LogService LogService { get; } = new LogService();

        internal CommandDispatcher CommandDispatcher { get; } = new CommandDispatcher();

        private NamedPipeServer? _pipeServer;

        private LogPipeServer? _logPipeServer;

        /// <summary>
        /// 工具端是否已连接。开发者开关等依赖工具端的功能可检查此属性。
        /// </summary>
        public bool IsToolConnected => _pipeServer?.IsClientConnected ?? false;

        public MainWindow()
        {
            InitializeComponent();
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
            _pipeServer.ClientStateChanged += OnToolConnectionChanged;
            _pipeServer.Start();

            _logPipeServer = new LogPipeServer(LogService);
            _logPipeServer.Start();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _pipeServer?.Stop();
            _pipeServer?.Dispose();
            _logPipeServer?.Stop();
            _logPipeServer?.Dispose();
        }

        private void OnToolConnectionChanged(object? sender, EventArgs e)
        {
            // 后期在这里处理开发者开关状态等逻辑
            LogService.Info($"工具端{(IsToolConnected ? "已连接" : "已断开")}");
        }

        // 此代码只做演示作用，后期会进行修改删除
        // 手动创建演示用骨骼结构，实际项目中将从配置文件加载角色数据
        private void InitializeSkeleton(double dpiScale)
        {
            // 此代码只做演示作用，后期会进行修改删除
            _skeleton = new Skeleton();

            // ==================== 根骨骼 ====================
            // 此代码只做演示作用，后期会进行修改删除
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
            // 此代码只做演示作用，后期会进行修改删除
            _skeleton.UpdateWorldTransforms();  // 计算所有骨骼的世界坐标
            // 此代码只做演示作用，后期会进行修改删除
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
    }
}
