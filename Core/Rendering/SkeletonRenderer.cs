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
        private readonly Dictionary<string, BitmapImage> _imageCache = new();
        private const double BONE_LINE_THICKNESS = 3;
        private const double JOINT_RADIUS = 4;

        /// <summary>
        /// 是否显示骨骼调试线框（默认关闭）。
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

            if (_imageCache.TryGetValue(path, out var cached))
                return cached;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                if (System.IO.Path.IsPathRooted(path))
                {
                    bitmap.UriSource = new Uri(path);
                }
                else
                {
                    bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                }

                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                _imageCache[path] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public override void Clear()
        {
            Context.Canvas.Children.Clear();
        }
    }
}
