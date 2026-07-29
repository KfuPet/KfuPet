using System.Text.Json;
using System.Windows;
using KfuPet.Models;
using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    /// <summary>
    /// 骨骼操作服务，供 IPC 通信层和内部模块调用。
    /// 外部程序请通过 Named Pipe / HTTP 等 IPC 方式调用。
    /// </summary>
    internal class SkeletonService : ICommandService
    {
        public string ServiceName => "skeleton";

        private Skeleton? _skeleton;

        /// <summary>
        /// 骨骼发生变化时触发，UI 层可订阅此事件刷新渲染。
        /// </summary>
        public event EventHandler? SkeletonChanged;

        /// <summary>
        /// 当前绑定的骨骼实例。
        /// </summary>
        public Skeleton? Skeleton => _skeleton;

        /// <summary>
        /// 绑定骨骼实例。
        /// </summary>
        public void BindSkeleton(Skeleton skeleton)
        {
            _skeleton = skeleton;
            RaiseSkeletonChanged();
        }

        /// <summary>
        /// 获取所有骨骼 ID 列表。
        /// </summary>
        public IReadOnlyList<string> GetBoneIds()
        {
            if (_skeleton == null) return Array.Empty<string>();
            return _skeleton.Bones.Select(b => b.Id).ToList();
        }

        /// <summary>
        /// 检查骨骼是否存在。
        /// </summary>
        public bool BoneExists(string boneId)
        {
            return _skeleton?.FindBone(boneId) != null;
        }

        /// <summary>
        /// 获取骨骼名称。
        /// </summary>
        public string? GetBoneName(string boneId)
        {
            return _skeleton?.FindBone(boneId)?.Name;
        }

        /// <summary>
        /// 获取骨骼的父骨骼 ID。
        /// </summary>
        public string? GetParentBoneId(string boneId)
        {
            return _skeleton?.FindBone(boneId)?.ParentId;
        }

        /// <summary>
        /// 获取骨骼的子骨骼 ID 列表。
        /// </summary>
        public IReadOnlyList<string> GetChildBoneIds(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return Array.Empty<string>();
            return bone.Children.Select(c => c.Id).ToList();
        }

        /// <summary>
        /// 设置骨骼的本地位置。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="x">X 偏移（逻辑像素）</param>
        /// <param name="y">Y 偏移（逻辑像素）</param>
        /// <returns>是否设置成功</returns>
        public bool SetPosition(string boneId, double x, double y)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalPosition = new Point(x, y);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 获取骨骼的本地位置。
        /// </summary>
        public Point? GetPosition(string boneId)
        {
            return _skeleton?.FindBone(boneId)?.LocalPosition;
        }

        /// <summary>
        /// 平移骨骼（相对当前位置偏移）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="deltaX">X 方向偏移量</param>
        /// <param name="deltaY">Y 方向偏移量</param>
        /// <returns>是否设置成功</returns>
        public bool Translate(string boneId, double deltaX, double deltaY)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            var pos = bone.LocalPosition;
            bone.LocalPosition = new Point(pos.X + deltaX, pos.Y + deltaY);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 设置骨骼的本地旋转（角度制）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="degrees">旋转角度（度）</param>
        /// <returns>是否设置成功</returns>
        public bool SetRotation(string boneId, double degrees)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalRotation = degrees * Math.PI / 180.0;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 获取骨骼的本地旋转（角度制）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <returns>旋转角度（度），如果骨骼不存在返回 null</returns>
        public double? GetRotation(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return null;
            return bone.LocalRotation * 180.0 / Math.PI;
        }

        /// <summary>
        /// 旋转骨骼（相对当前角度旋转）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="deltaDegrees">相对旋转角度（度）</param>
        /// <returns>是否设置成功</returns>
        public bool Rotate(string boneId, double deltaDegrees)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalRotation += deltaDegrees * Math.PI / 180.0;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 设置骨骼的本地缩放。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="scaleX">X 方向缩放</param>
        /// <param name="scaleY">Y 方向缩放</param>
        /// <returns>是否设置成功</returns>
        public bool SetScale(string boneId, double scaleX, double scaleY)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalScale = new Point(scaleX, scaleY);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 获取骨骼的本地缩放。
        /// </summary>
        public Point? GetScale(string boneId)
        {
            return _skeleton?.FindBone(boneId)?.LocalScale;
        }

        /// <summary>
        /// 设置骨骼的激活状态。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <param name="isActive">是否激活</param>
        /// <returns>是否设置成功</returns>
        public bool SetActive(string boneId, bool isActive)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.IsActive = isActive;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 获取骨骼的激活状态。
        /// </summary>
        public bool? IsActive(string boneId)
        {
            return _skeleton?.FindBone(boneId)?.IsActive;
        }

        /// <summary>
        /// 重置骨骼为初始状态（位置归零，旋转归零，缩放为 1）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <returns>是否重置成功</returns>
        public bool ResetBone(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalPosition = new Point(0, 0);
            bone.LocalRotation = 0;
            bone.LocalScale = new Point(1, 1);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 重置所有骨骼为初始状态。
        /// </summary>
        public void ResetAll()
        {
            if (_skeleton == null) return;

            foreach (var bone in _skeleton.Bones)
            {
                bone.LocalPosition = new Point(0, 0);
                bone.LocalRotation = 0;
                bone.LocalScale = new Point(1, 1);
                bone.IsActive = true;
            }
            UpdateAndNotify();
        }

        /// <summary>
        /// 批量操作骨骼，所有修改完成后只触发一次更新。
        /// </summary>
        /// <param name="action">批量操作的委托</param>
        public void Batch(Action<SkeletonService> action)
        {
            if (action == null || _skeleton == null) return;
            action(this);
            UpdateAndNotify();
        }

        /// <summary>
        /// 获取骨骼的世界位置。
        /// </summary>
        public Point? GetWorldPosition(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null || bone.WorldTransform == null) return null;
            return bone.WorldTransform.Transform(new Point(0, 0));
        }

        private void UpdateAndNotify()
        {
            _skeleton?.UpdateWorldTransforms();
            RaiseSkeletonChanged();
        }

        private void RaiseSkeletonChanged()
        {
            SkeletonChanged?.Invoke(this, EventArgs.Empty);
        }

        public CommandResponse Execute(string action, Dictionary<string, object>? parameters)
        {
            switch (action.ToLowerInvariant())
            {
                case "getboneids":
                    return CommandResponse.Ok(GetBoneIds());

                case "boneexists":
                    return CommandResponse.Ok(BoneExists(Str(parameters, "boneId")));

                case "getbonename":
                    return CommandResponse.Ok(GetBoneName(Str(parameters, "boneId")));

                case "getparentboneid":
                    return CommandResponse.Ok(GetParentBoneId(Str(parameters, "boneId")));

                case "getchildboneids":
                    return CommandResponse.Ok(GetChildBoneIds(Str(parameters, "boneId")));

                case "setposition":
                    return CommandResponse.Ok(SetPosition(
                        Str(parameters, "boneId"), Dbl(parameters, "x"), Dbl(parameters, "y")));

                case "getposition":
                    var pos = GetPosition(Str(parameters, "boneId"));
                    return CommandResponse.Ok(pos.HasValue ? new { x = pos.Value.X, y = pos.Value.Y } : null);

                case "translate":
                    return CommandResponse.Ok(Translate(
                        Str(parameters, "boneId"), Dbl(parameters, "deltaX"), Dbl(parameters, "deltaY")));

                case "setrotation":
                    return CommandResponse.Ok(SetRotation(
                        Str(parameters, "boneId"), Dbl(parameters, "degrees")));

                case "getrotation":
                    return CommandResponse.Ok(GetRotation(Str(parameters, "boneId")));

                case "rotate":
                    return CommandResponse.Ok(Rotate(
                        Str(parameters, "boneId"), Dbl(parameters, "deltaDegrees")));

                case "setscale":
                    return CommandResponse.Ok(SetScale(
                        Str(parameters, "boneId"), Dbl(parameters, "scaleX"), Dbl(parameters, "scaleY")));

                case "getscale":
                    var scale = GetScale(Str(parameters, "boneId"));
                    return CommandResponse.Ok(scale.HasValue ? new { x = scale.Value.X, y = scale.Value.Y } : null);

                case "setactive":
                    return CommandResponse.Ok(SetActive(
                        Str(parameters, "boneId"), Bool(parameters, "isActive")));

                case "isactive":
                    return CommandResponse.Ok(IsActive(Str(parameters, "boneId")));

                case "resetbone":
                    return CommandResponse.Ok(ResetBone(Str(parameters, "boneId")));

                case "resetall":
                    ResetAll();
                    return CommandResponse.Ok();

                case "getworldposition":
                    var wp = GetWorldPosition(Str(parameters, "boneId"));
                    return CommandResponse.Ok(wp.HasValue ? new { x = wp.Value.X, y = wp.Value.Y } : null);

                case "batch":
                    var operations = parameters?.GetValueOrDefault("operations") as JsonElement?;
                    if (operations.HasValue && operations.Value.ValueKind == JsonValueKind.Array)
                    {
                        Batch(svc =>
                        {
                            var skelSvc = (SkeletonService)svc;
                            foreach (var op in operations.Value.EnumerateArray())
                            {
                                var opAction = op.GetProperty("action").GetString();
                                var opParams = op.GetProperty("params").Deserialize<Dictionary<string, object>>();
                                if (opAction != null)
                                {
                                    skelSvc.Execute(opAction, opParams);
                                }
                            }
                        });
                        return CommandResponse.Ok();
                    }
                    return CommandResponse.Fail("Invalid batch operations");

                default:
                    return CommandResponse.Fail($"Unknown action: {action}");
            }
        }

        private static string Str(Dictionary<string, object>? p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return string.Empty;
            return v?.ToString() ?? string.Empty;
        }

        private static double Dbl(Dictionary<string, object>? p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return 0;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetDouble();
            if (double.TryParse(v?.ToString(), out var d))
                return d;
            return 0;
        }

        private static bool Bool(Dictionary<string, object>? p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return false;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
            if (v is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
            if (bool.TryParse(v?.ToString(), out var b)) return b;
            return false;
        }
    }
}
