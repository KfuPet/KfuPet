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
            var localTransform = Matrix3x3.Rotation(bone.LocalRotation) *
                                 Matrix3x3.Translation(bone.LocalPosition.X, bone.LocalPosition.Y) *
                                 Matrix3x3.Scale(bone.LocalScale.X, bone.LocalScale.Y);

            bone.WorldTransform = parentTransform * localTransform;

            foreach (var child in bone.Children)
            {
                UpdateBoneTransform(child, bone.WorldTransform);
            }
        }
    }
}