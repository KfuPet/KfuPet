using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KfuPet.Models;
using KfuPet.Core.Rendering;

namespace KfuPet.Controls
{
    public partial class CharacterCanvas : UserControl
    {
        private SkeletonRenderer? _skeletonRenderer;
        private RenderContext? _renderContext;

        // 位图 BGRA 像素缓存：用于悬停时的不透明像素命中检测，避免重复解码
        private readonly Dictionary<BitmapSource, (byte[] Pixels, int PixelWidth, int PixelHeight, int Stride)> _alphaCache = new();

        public static readonly DependencyProperty SkeletonProperty =
            DependencyProperty.Register(nameof(Skeleton), typeof(Skeleton), typeof(CharacterCanvas),
                new PropertyMetadata(null, OnSkeletonChanged));

        public Skeleton? Skeleton
        {
            get => (Skeleton?)GetValue(SkeletonProperty);
            set => SetValue(SkeletonProperty, value);
        }

        /// <summary>
        /// 是否显示骨骼调试线框。
        /// </summary>
        public bool ShowDebugBones
        {
            get => _skeletonRenderer?.ShowDebugBones ?? false;
            set
            {
                if (_skeletonRenderer != null)
                {
                    _skeletonRenderer.ShowDebugBones = value;
                    Render();
                }
            }
        }

        public CharacterCanvas()
        {
            InitializeComponent();
            Loaded += CharacterCanvas_Loaded;
            InitializeRenderer();
        }

        private void CharacterCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            var parent = Window.GetWindow(this);
            if (parent != null)
            {
                RenderCanvas.Width = parent.ActualWidth;
                RenderCanvas.Height = parent.ActualHeight;
            }
        }

        private void InitializeRenderer()
        {
            _renderContext = new RenderContext(RenderCanvas);
            _skeletonRenderer = new SkeletonRenderer(_renderContext);
        }

        private static void OnSkeletonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CharacterCanvas canvas)
            {
                canvas.Render();
            }
        }

        public void Render()
        {
            if (_skeletonRenderer != null && Skeleton != null)
            {
                Skeleton.UpdateWorldTransforms();
                _skeletonRenderer.Render(Skeleton);
            }
        }

        public void Clear()
        {
            _skeletonRenderer?.Clear();
        }

        /// <summary>
        /// 判断画布上的点是否命中角色的不透明区域（用于悬停交互，透明部分不响应）。
        /// 按 Z 序从顶层往下检查各附件图片的像素透明度。
        /// </summary>
        public bool HitTestOpaque(Point canvasPoint)
        {
            foreach (var image in RenderCanvas.Children.OfType<Image>()
                         .OrderByDescending(Panel.GetZIndex))
            {
                if (image.Source is not BitmapSource source)
                {
                    continue;
                }

                // 画布坐标 → 图片元素本地坐标（含旋转等 RenderTransform）
                var inverse = image.TransformToAncestor(RenderCanvas).Inverse;
                if (inverse == null || !inverse.TryTransform(canvasPoint, out var local))
                {
                    continue;
                }

                if (local.X < 0 || local.Y < 0 || local.X >= image.ActualWidth || local.Y >= image.ActualHeight)
                {
                    continue;
                }

                // 本地坐标 → 位图像素坐标
                var px = (int)(local.X * source.PixelWidth / image.ActualWidth);
                var py = (int)(local.Y * source.PixelHeight / image.ActualHeight);

                if (GetAlpha(source, px, py) > 16)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取位图指定像素的 Alpha 值，像素数据统一转成 BGRA32 后缓存。
        /// </summary>
        private byte GetAlpha(BitmapSource source, int px, int py)
        {
            if (!_alphaCache.TryGetValue(source, out var data))
            {
                var bgra = source.Format == PixelFormats.Bgra32
                    ? source
                    : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

                var stride = bgra.PixelWidth * 4;
                var pixels = new byte[bgra.PixelHeight * stride];
                bgra.CopyPixels(pixels, stride, 0);
                data = (pixels, bgra.PixelWidth, bgra.PixelHeight, stride);
                _alphaCache[source] = data;
            }

            if (px < 0 || py < 0 || px >= data.PixelWidth || py >= data.PixelHeight)
            {
                return 0;
            }

            return data.Pixels[py * data.Stride + px * 4 + 3];
        }
    }
}