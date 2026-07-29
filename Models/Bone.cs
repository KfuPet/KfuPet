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