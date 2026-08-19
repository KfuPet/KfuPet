using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KfuPet.Core.Math;
using KfuPet.Models;

namespace KfuPet.Core.Rendering
{
    public class SkeletonRenderer : Renderer
    {
        private const int MAX_IMAGE_CACHE = 256;

        private static readonly string _resourceRootDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources");

        private readonly Dictionary<string, (BitmapImage Image, DateTime LastWriteUtc)> _imageCache = new();

        private const double BONE_LINE_THICKNESS = 3;
        private const double JOINT_RADIUS = 4;

        /// <summary>
        /// 是否显示骨骼调试线框（默认关闭）。
        /// 这是给创作者对照模型资源、定位骨骼摆放位置的参考工具，长期保留，不会移除。
        /// 由开发工具端通过开关控制启用。
        /// </summary>
        public bool ShowDebugBones { get; set; }

        public SkeletonRenderer(RenderContext context) : base(context)
        {
        }

        public override void Render(Skeleton skeleton)
        {
            Clear();

            if (ShowDebugBones)
            {
                RenderDebugBones(skeleton);
            }

            var attachments = new List<(Attachment Attachment, Matrix3x3 WorldTransform, double WorldRotation)>();

            foreach (var bone in skeleton.Bones)
            {
                if (!bone.IsActive || bone.WorldTransform == null) continue;

                foreach (var attachment in bone.Attachments)
                {
                    if (!attachment.Visible) continue;

                    var wt = bone.WorldTransform;
                    var worldRotation = System.Math.Atan2(wt.M21, wt.M11);
                    attachments.Add((attachment, wt, worldRotation));
                }
            }

            // 按 ZOrder 排序后渲染
            attachments.Sort((a, b) => a.Attachment.ZOrder.CompareTo(b.Attachment.ZOrder));

            int zIndex = 0;
            foreach (var (attachment, worldTransform, worldRotation) in attachments)
            {
                RenderAttachment(attachment, worldTransform, worldRotation, zIndex++);
            }
        }

        /// <summary>
        /// 绘制骨骼调试线框（骨骼连线与关节圆点），供创作者对照模型资源定位骨骼。
        /// </summary>
        private void RenderDebugBones(Skeleton skeleton)
        {
            foreach (var bone in skeleton.Bones)
            {
                if (!bone.IsActive || bone.WorldTransform == null) continue;

                var pos = bone.WorldTransform.Transform(new Point(0, 0));

                // 绘制骨骼连线
                if (bone.Parent != null && bone.Parent.IsActive && bone.Parent.WorldTransform != null)
                {
                    var parentPos = bone.Parent.WorldTransform.Transform(new Point(0, 0));
                    var line = new Line
                    {
                        X1 = parentPos.X, Y1 = parentPos.Y,
                        X2 = pos.X, Y2 = pos.Y,
                        Stroke = Brushes.DodgerBlue,
                        StrokeThickness = BONE_LINE_THICKNESS
                    };
                    Canvas.SetZIndex(line, 1000);
                    Context.Canvas.Children.Add(line);
                }

                // 绘制关节圆点
                var ellipse = new Ellipse
                {
                    Width = JOINT_RADIUS * 2,
                    Height = JOINT_RADIUS * 2,
                    Fill = Brushes.DodgerBlue
                };
                Canvas.SetLeft(ellipse, pos.X - JOINT_RADIUS);
                Canvas.SetTop(ellipse, pos.Y - JOINT_RADIUS);
                Canvas.SetZIndex(ellipse, 1001);
                Context.Canvas.Children.Add(ellipse);
            }
        }

        private void RenderAttachment(Attachment attachment, Matrix3x3 worldTransform, double worldRotation, int zIndex)
        {
            var image = LoadImageForAttachment(attachment);
            if (image == null) return;

            // 附件世界位置 = 骨骼世界变换 * 附件偏移
            var worldPos = worldTransform.Transform(attachment.Offset);

            // 图片放在 Canvas 上，通过 RenderTransform 旋转
            var imageControl = new Image
            {
                Source = image,
                Width = image.PixelWidth * attachment.ScaleX,
                Height = image.PixelHeight * attachment.ScaleY
            };

            // 按 Pivot 放置：左上角位置 = 世界位置 - 缩放后尺寸 * 锚点
            Canvas.SetLeft(imageControl, worldPos.X - image.PixelWidth * attachment.ScaleX * attachment.Pivot.X);
            Canvas.SetTop(imageControl, worldPos.Y - image.PixelHeight * attachment.ScaleY * attachment.Pivot.Y);
            Canvas.SetZIndex(imageControl, zIndex);

            imageControl.RenderTransformOrigin = new Point(attachment.Pivot.X, attachment.Pivot.Y);
            imageControl.RenderTransform = new RotateTransform(worldRotation * 180.0 / System.Math.PI);

            Context.Canvas.Children.Add(imageControl);
        }

        private BitmapImage? LoadImageForAttachment(Attachment attachment)
        {
            var path = attachment.GetCurrentResourcePath();
            if (string.IsNullOrEmpty(path)) return null;

            var fullPath = ResolveResourcePath(path);
            var lastWriteUtc = GetLastWriteTimeUtc(fullPath);

            // 文件未变化时复用缓存，避免重复解码；文件更新后自动重新加载。
            if (_imageCache.TryGetValue(fullPath, out var entry) && entry.LastWriteUtc == lastWriteUtc)
                return entry.Image;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();

                if (_imageCache.Count >= MAX_IMAGE_CACHE)
                    _imageCache.Clear();

                _imageCache[fullPath] = (bitmap, lastWriteUtc);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将相对资源路径解析到统一的资源根目录（{BaseDirectory}/Resources）下。
        /// </summary>
        private static string ResolveResourcePath(string path)
        {
            return System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.Combine(_resourceRootDir, path);
        }

        /// <summary>
        /// 获取文件最后写入时间，用于缓存失效判断；文件不存在或不可读时返回 MinValue。
        /// </summary>
        private static DateTime GetLastWriteTimeUtc(string fullPath)
        {
            try
            {
                var fileInfo = new FileInfo(fullPath);
                return fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public override void Clear()
        {
            Context.Canvas.Children.Clear();
        }
    }
}
