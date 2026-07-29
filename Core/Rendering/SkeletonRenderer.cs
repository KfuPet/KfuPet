using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KfuPet.Models;
using KfuPet.Core.Math;

namespace KfuPet.Core.Rendering
{
    // 此代码只做演示作用，后期会进行修改删除
    // 使用黑色粗线条和圆点模拟骨骼结构，实际项目中将使用图片资源渲染角色附件
    public class SkeletonRenderer : Renderer
    {
        // 此代码只做演示作用，后期会进行修改删除
        private readonly Brush _boneBrush;
        // 此代码只做演示作用，后期会进行修改删除
        private const double BONE_LINE_THICKNESS = 4;
        // 此代码只做演示作用，后期会进行修改删除
        private const double JOINT_RADIUS = 6;

        public SkeletonRenderer(RenderContext context) : base(context)
        {
            // 此代码只做演示作用，后期会进行修改删除
            _boneBrush = Brushes.Black;
        }

        public override void Render(Skeleton skeleton)
        {
            Clear();

            if (skeleton.Root != null)
            {
                RenderBoneRecursive(skeleton.Root);
            }
        }

        private void RenderBoneRecursive(Bone bone)
        {
            if (!bone.IsActive) return;

            var worldTransform = bone.WorldTransform;
            if (worldTransform == null) return;

            var bonePosition = worldTransform.Transform(new Point(0, 0));

            if (bone.Parent != null && bone.Parent.WorldTransform != null)
            {
                var parentPosition = bone.Parent.WorldTransform.Transform(new Point(0, 0));
                DrawBoneLine(parentPosition, bonePosition);
            }

            DrawJoint(bonePosition);

            foreach (var child in bone.Children)
            {
                RenderBoneRecursive(child);
            }
        }

        // 此代码只做演示作用，后期会进行修改删除
        // 使用黑色粗线条绘制骨骼连接
        private void DrawBoneLine(Point start, Point end)
        {
            var line = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                // 此代码只做演示作用，后期会进行修改删除
                Stroke = _boneBrush,
                // 此代码只做演示作用，后期会进行修改删除
                StrokeThickness = BONE_LINE_THICKNESS
            };

            Canvas.SetZIndex(line, 0);
            Context.Canvas.Children.Add(line);
        }

        // 此代码只做演示作用，后期会进行修改删除
        // 使用黑色圆点绘制关节点
        private void DrawJoint(Point position)
        {
            var ellipse = new Ellipse
            {
                // 此代码只做演示作用，后期会进行修改删除
                Width = JOINT_RADIUS * 2,
                // 此代码只做演示作用，后期会进行修改删除
                Height = JOINT_RADIUS * 2,
                // 此代码只做演示作用，后期会进行修改删除
                Fill = _boneBrush
            };

            Canvas.SetLeft(ellipse, position.X - JOINT_RADIUS);
            Canvas.SetTop(ellipse, position.Y - JOINT_RADIUS);
            Canvas.SetZIndex(ellipse, 1);

            Context.Canvas.Children.Add(ellipse);
        }

        public override void Clear()
        {
            Context.Canvas.Children.Clear();
        }
    }
}