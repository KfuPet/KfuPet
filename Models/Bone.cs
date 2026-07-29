using System.Windows;
using KfuPet.Core.Math;

namespace KfuPet.Models
{
    public class Bone
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? ParentId { get; set; }

        public Bone? Parent { get; set; }

        public List<Bone> Children { get; } = new List<Bone>();

        public Point LocalPosition { get; set; } = new Point();

        public double LocalRotation { get; set; }

        public Point LocalScale { get; set; } = new Point(1, 1);

        public Matrix3x3? WorldTransform { get; set; }

        public List<Attachment> Attachments { get; } = new List<Attachment>();

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 骨骼的默认值，用于"恢复默认"功能。
        /// 在骨骼首次添加到 Skeleton 时自动拍摄。
        /// </summary>
        public Point DefaultPosition { get; private set; }

        public double DefaultRotation { get; private set; }

        public Point DefaultScale { get; private set; } = new Point(1, 1);

        public bool DefaultIsActive { get; private set; } = true;

        /// <summary>
        /// 将当前 Local* 值保存为默认值。由 Skeleton.AddBone 内部调用。
        /// </summary>
        internal void CaptureDefaults()
        {
            DefaultPosition = LocalPosition;
            DefaultRotation = LocalRotation;
            DefaultScale = LocalScale;
            DefaultIsActive = IsActive;
        }

        public void AddChild(Bone child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public void AddAttachment(Attachment attachment)
        {
            attachment.Bone = this;
            Attachments.Add(attachment);
        }
    }
}