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

            // 先平移到骨骼自身位置，再绕自身旋转（T·R·S）：
            // root 的 LocalPosition 是世界锚点，普通骨骼的 LocalPosition 是相对父骨骼的关节位置，
            // 两者都不应被自身旋转带着走，否则转动关节时该部位会整体平移，接缝补不上。
            var localTransform = translation * rotation * scale;

            bone.WorldTransform = parentTransform * localTransform;

            foreach (var child in bone.Children)
            {
                UpdateBoneTransform(child, bone.WorldTransform);
            }
        }
    }
}
