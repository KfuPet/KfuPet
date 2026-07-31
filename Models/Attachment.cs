using System.Windows;

namespace KfuPet.Models
{
    public class Attachment
    {
        public string Id { get; set; } = string.Empty;

        public string BoneId { get; set; } = string.Empty;

        public Bone? Bone { get; set; }

        public string Name { get; set; } = string.Empty;

        public AttachmentSet Set { get; } = new AttachmentSet();

        public string? CurrentResource => Set.CurrentResourceId;

        public Point Offset { get; set; } = new Point();

        public Point Pivot { get; set; } = new Point(0.5, 0.5);

        public int ZOrder { get; set; }

        public bool Visible { get; set; } = true;

        public double ScaleX { get; set; } = 1.0;

        public double ScaleY { get; set; } = 1.0;

        public string? GetCurrentResourcePath()
        {
            return Set.GetCurrentResourcePath();
        }

        public void SetResource(string resourceId)
        {
            Set.SetResource(resourceId);
        }
    }
}