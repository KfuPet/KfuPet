using System.Windows;
using KfuPet.Core.Math;

namespace KfuPet.Models
{
    public class Skeleton
    {
        public List<Bone> Bones { get; } = new List<Bone>();

        public Bone? Root { get; private set; }

        public Dictionary<string, Bone> BoneMap { get; } = new Dictionary<string, Bone>();

        public void AddBone(Bone bone)
        {
            bone.CaptureDefaults();
            Bones.Add(bone);
            BoneMap[bone.Id] = bone;

            if (bone.ParentId == null)
            {
                Root = bone;
            }
            else if (BoneMap.TryGetValue(bone.ParentId, out var parent))
            {
                parent.AddChild(bone);
            }
        }

        public Bone? FindBone(string id)
        {
            return BoneMap.TryGetValue(id, out var bone) ? bone : null;
        }

        public Attachment? FindAttachment(string attachmentId)
        {
            foreach (var bone in Bones)
            {
                var attachment = bone.Attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment != null) return attachment;
            }
            return null;
        }

        public bool RemoveAttachment(string attachmentId)
        {
            foreach (var bone in Bones)
            {
                var attachment = bone.Attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment != null)
                {
                    bone.Attachments.Remove(attachment);
                    return true;
                }
            }
            return false;
        }

        public void BuildHierarchy()
        {
            foreach (var bone in Bones)
            {
                if (bone.ParentId != null && bone.Parent == null)
                {
                    if (BoneMap.TryGetValue(bone.ParentId, out var parent))
                    {
                        parent.AddChild(bone);
                    }
                }
            }
        }

        public void UpdateWorldTransforms()
        {
            if (Root != null)
            {
                UpdateBoneTransform(Root, Matrix3x3.Identity);
            }
        }

        private void UpdateBoneTransform(Bone bone, Matrix3x3 parentTransform)
        {
            var rotation = Matrix3x3.Rotation(bone.LocalRotation);
            var translation = Matrix3x3.Translation(bone.LocalPosition.X, bone.LocalPosition.Y);
            var scale = Matrix3x3.Scale(bone.LocalScale.X, bone.LocalScale.Y);

            // Root 的位置是世界锚点，旋转时不能连同锚点一起绕画布原点移动。
            // 普通骨骼的旋转则作用于“父节点到当前节点”的骨骼线段。
            var localTransform = bone.Parent == null
                ? translation * rotation * scale
                : rotation * translation * scale;

            bone.WorldTransform = parentTransform * localTransform;

            foreach (var child in bone.Children)
            {
                UpdateBoneTransform(child, bone.WorldTransform);
            }
        }
    }
}
