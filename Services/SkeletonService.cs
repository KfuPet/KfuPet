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
        /// 是否显示骨骼调试线框（默认关闭）。
        /// </summary>
        public bool ShowDebugSkeleton { get; private set; }

        /// <summary>
        /// 调试线框状态变化时触发。
        /// </summary>
        public event EventHandler? DebugSkeletonChanged;

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
        /// 恢复骨骼到默认状态（使用 Bone 自带的 Default* 值，
        /// 这些值在骨骼首次 AddBone 时自动固化）。
        /// </summary>
        /// <param name="boneId">骨骼 ID</param>
        /// <returns>是否恢复成功</returns>
        public bool ResetBone(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return false;

            bone.LocalPosition = bone.DefaultPosition;
            bone.LocalRotation = bone.DefaultRotation;
            bone.LocalScale = bone.DefaultScale;
            bone.IsActive = bone.DefaultIsActive;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 恢复所有骨骼到默认状态。
        /// </summary>
        public void ResetAll()
        {
            if (_skeleton == null) return;

            foreach (var bone in _skeleton.Bones)
            {
                bone.LocalPosition = bone.DefaultPosition;
                bone.LocalRotation = bone.DefaultRotation;
                bone.LocalScale = bone.DefaultScale;
                bone.IsActive = bone.DefaultIsActive;
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

        /// <summary>
        /// 为指定骨骼添加图片附件。
        /// </summary>
        public Attachment? AddAttachment(string boneId, string attachmentId, string name,
            string resourcePath, double offsetX = 0, double offsetY = 0,
            double pivotX = 0.5, double pivotY = 0.5, int zOrder = 0)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return null;

            var attachment = new Attachment
            {
                Id = attachmentId,
                BoneId = boneId,
                Name = name,
                Offset = new Point(offsetX, offsetY),
                Pivot = new Point(pivotX, pivotY),
                ZOrder = zOrder
            };
            attachment.Set.DefaultResource = "default";
            attachment.Set.Resources["default"] = resourcePath;
            attachment.Set.CurrentResourceId = "default";

            bone.AddAttachment(attachment);
            UpdateAndNotify();
            return attachment;
        }

        /// <summary>
        /// 移除骨骼附件。
        /// </summary>
        public bool RemoveAttachment(string attachmentId)
        {
            if (_skeleton == null) return false;

            var attachment = _skeleton.FindAttachment(attachmentId);
            if (attachment == null) return false;

            var resourcePath = attachment.GetCurrentResourcePath();

            if (_skeleton.RemoveAttachment(attachmentId))
            {
                TryCleanupResource(resourcePath);
                UpdateAndNotify();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 如果缓存文件没有被其他附件引用，则删除之。
        /// </summary>
        private void TryCleanupResource(string? resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath) || _skeleton == null) return;

            // 仅清理缓存目录下的文件，不碰用户手动指定的路径
            if (!IsPathInCache(resourcePath))
                return;

            // 检查是否有其他附件还在引用该文件
            foreach (var bone in _skeleton.Bones)
            {
                foreach (var att in bone.Attachments)
                {
                    var p = att.GetCurrentResourcePath();
                    if (!string.IsNullOrEmpty(p) &&
                        string.Equals(System.IO.Path.GetFullPath(p), System.IO.Path.GetFullPath(resourcePath), StringComparison.OrdinalIgnoreCase))
                    {
                        return; // 仍有引用，不删
                    }
                }
            }

            try { System.IO.File.Delete(resourcePath); } catch { }
        }

        /// <summary>
        /// 显式删除缓存目录下的资源文件。
        /// </summary>
        public bool DeleteResource(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return false;
            if (!IsPathInCache(resourcePath)) return false;
            if (!System.IO.File.Exists(resourcePath)) return false;

            try
            {
                System.IO.File.Delete(resourcePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPathInCache(string path)
        {
            var normalizedCache = System.IO.Path.GetFullPath(ResourceCacheDir);
            var normalizedPath = System.IO.Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedCache, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 切换附件的当前资源。
        /// </summary>
        public bool SetAttachmentResource(string attachmentId, string resourceId, string resourcePath)
        {
            var attachment = _skeleton?.FindAttachment(attachmentId);
            if (attachment == null) return false;

            attachment.Set.Resources[resourceId] = resourcePath;
            attachment.Set.SetResource(resourceId);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 设置附件偏移。
        /// </summary>
        public bool SetAttachmentOffset(string attachmentId, double x, double y)
        {
            var attachment = _skeleton?.FindAttachment(attachmentId);
            if (attachment == null) return false;

            attachment.Offset = new Point(x, y);
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 设置附件显隐。
        /// </summary>
        public bool SetAttachmentVisible(string attachmentId, bool visible)
        {
            var attachment = _skeleton?.FindAttachment(attachmentId);
            if (attachment == null) return false;

            attachment.Visible = visible;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 设置图片 X/Y 轴缩放。
        /// </summary>
        public bool SetAttachmentScale(string attachmentId, double scaleX, double scaleY)
        {
            var attachment = _skeleton?.FindAttachment(attachmentId);
            if (attachment == null) return false;

            attachment.ScaleX = scaleX;
            attachment.ScaleY = scaleY;
            UpdateAndNotify();
            return true;
        }

        /// <summary>
        /// 获取图片缩放值。
        /// </summary>
        public (double ScaleX, double ScaleY)? GetAttachmentScale(string attachmentId)
        {
            var attachment = _skeleton?.FindAttachment(attachmentId);
            if (attachment == null) return null;
            return (attachment.ScaleX, attachment.ScaleY);
        }

        /// <summary>
        /// 获取附件信息。
        /// </summary>
        public Attachment? GetAttachment(string attachmentId)
        {
            return _skeleton?.FindAttachment(attachmentId);
        }

        /// <summary>
        /// 获取指定骨骼的所有附件 ID 列表。
        /// </summary>
        public IReadOnlyList<string> GetBoneAttachments(string boneId)
        {
            var bone = _skeleton?.FindBone(boneId);
            if (bone == null) return Array.Empty<string>();
            return bone.Attachments.Select(a => a.Id).ToList();
        }

        /// <summary>
        /// 开关骨骼调试线框显示。
        /// </summary>
        public void SetDebugSkeleton(bool show)
        {
            if (ShowDebugSkeleton == show) return;
            ShowDebugSkeleton = show;
            DebugSkeletonChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 获取骨骼调试线框显示状态。
        /// </summary>
        public bool GetDebugSkeleton() => ShowDebugSkeleton;

        private static readonly string ResourceCacheDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Cache");

        /// <summary>
        /// 退出时清理整个缓存目录。
        /// </summary>
        public static void CleanupCache()
        {
            try
            {
                if (System.IO.Directory.Exists(ResourceCacheDir))
                    System.IO.Directory.Delete(ResourceCacheDir, true);
            }
            catch
            {
                // 忽略清理失败（文件被占用等）
            }
        }

        /// <summary>
        /// 上传图片资源到本地缓存，返回可用的文件路径。
        /// </summary>
        /// <param name="base64Data">base64 编码的图片数据</param>
        /// <param name="boneId">可选骨骼 ID，传入后图片存入 Resources/Cache/{boneId}/ 子目录</param>
        public string? UploadResource(string base64Data, string? boneId = null)
        {
            try
            {
                // 解析 data URI 或纯 base64
                string base64 = base64Data;
                string extension = ".png";

                if (base64Data.StartsWith("data:image/"))
                {
                    var headerEnd = base64Data.IndexOf(";base64,");
                    if (headerEnd > 0)
                    {
                        var mime = base64Data[11..headerEnd]; // "data:image/" = 11 chars
                        extension = mime switch
                        {
                            "png" => ".png",
                            "jpeg" => ".jpg",
                            "gif" => ".gif",
                            "webp" => ".webp",
                            "bmp" => ".bmp",
                            _ => ".png"
                        };
                        base64 = base64Data[(headerEnd + 8)..];
                    }
                }

                var bytes = Convert.FromBase64String(base64);

                var targetDir = string.IsNullOrEmpty(boneId)
                    ? ResourceCacheDir
                    : System.IO.Path.Combine(ResourceCacheDir, boneId);
                System.IO.Directory.CreateDirectory(targetDir);

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var filePath = System.IO.Path.Combine(targetDir, fileName);
                System.IO.File.WriteAllBytes(filePath, bytes);

                return filePath;
            }
            catch
            {
                return null;
            }
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

                case "addattachment":
                    var att = AddAttachment(
                        Str(parameters, "boneId"), Str(parameters, "attachmentId"),
                        Str(parameters, "name"), Str(parameters, "resourcePath"),
                        Dbl(parameters, "offsetX"), Dbl(parameters, "offsetY"),
                        Dbl(parameters, "pivotX", 0.5), Dbl(parameters, "pivotY", 0.5),
                        (int)Dbl(parameters, "zOrder"));
                    return CommandResponse.Ok(att != null);

                case "removeattachment":
                    return CommandResponse.Ok(RemoveAttachment(Str(parameters, "attachmentId")));

                case "setattachmentresource":
                    return CommandResponse.Ok(SetAttachmentResource(
                        Str(parameters, "attachmentId"), Str(parameters, "resourceId"),
                        Str(parameters, "resourcePath")));

                case "setattachmentoffset":
                    return CommandResponse.Ok(SetAttachmentOffset(
                        Str(parameters, "attachmentId"), Dbl(parameters, "x"), Dbl(parameters, "y")));

                case "setattachmentvisible":
                    return CommandResponse.Ok(SetAttachmentVisible(
                        Str(parameters, "attachmentId"), Bool(parameters, "visible")));

                case "setattachmentscale":
                    return CommandResponse.Ok(SetAttachmentScale(
                        Str(parameters, "attachmentId"),
                        Dbl(parameters, "scaleX", 1.0),
                        Dbl(parameters, "scaleY", 1.0)));

                case "getattachmentscale":
                    var sc = GetAttachmentScale(Str(parameters, "attachmentId"));
                    if (sc == null) return CommandResponse.Ok(null);
                    return CommandResponse.Ok(new { scaleX = sc.Value.ScaleX, scaleY = sc.Value.ScaleY });

                case "getattachment":
                    var ga = GetAttachment(Str(parameters, "attachmentId"));
                    if (ga == null) return CommandResponse.Ok(null);
                    return CommandResponse.Ok(new
                    {
                        id = ga.Id,
                        boneId = ga.BoneId,
                        name = ga.Name,
                        resourcePath = ga.GetCurrentResourcePath(),
                        offsetX = ga.Offset.X, offsetY = ga.Offset.Y,
                        pivotX = ga.Pivot.X, pivotY = ga.Pivot.Y,
                        zOrder = ga.ZOrder,
                        visible = ga.Visible
                    });

                case "getboneattachments":
                    return CommandResponse.Ok(GetBoneAttachments(Str(parameters, "boneId")));

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

                case "uploadresource":
                    var uploadedPath = UploadResource(Str(parameters, "base64Data"),
                        Str(parameters, "boneId", null));
                    if (uploadedPath == null)
                        return CommandResponse.Fail("Failed to decode or save image data");
                    return CommandResponse.Ok(new { path = uploadedPath });

                case "deleteresource":
                    return CommandResponse.Ok(DeleteResource(Str(parameters, "resourcePath")));

                case "setdebugskeleton":
                    SetDebugSkeleton(Bool(parameters, "show"));
                    return CommandResponse.Ok();

                case "getdebugskeleton":
                    return CommandResponse.Ok(GetDebugSkeleton());

                case "ping":
                    return CommandResponse.Ok();

                default:
                    return CommandResponse.Fail($"Unknown action: {action}");
            }
        }

        private static string Str(Dictionary<string, object>? p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return string.Empty;
            return v?.ToString() ?? string.Empty;
        }

        private static string? Str(Dictionary<string, object>? p, string key, string? fallback)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return fallback;
            return v?.ToString() ?? fallback;
        }

        private static double Dbl(Dictionary<string, object>? p, string key, double defaultValue = 0)
        {
            if (p == null || !p.TryGetValue(key, out var v)) return defaultValue;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetDouble();
            if (double.TryParse(v?.ToString(), out var d))
                return d;
            return defaultValue;
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
