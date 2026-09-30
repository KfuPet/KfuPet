using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 角色包附件配置（attachments.json）的读写：把角色包里的部位图片挂载到骨骼上，
    /// 以及把当前骨骼上的附件状态导出回角色包，供创作者用开发者工具调整后固化。
    /// 位置遵循角色包约定：Characters/&lt;角色名&gt;/attachments.json，图片放在 images/ 下。
    /// </summary>
    internal static class CharacterAttachmentService
    {
        /// <summary>附件配置文件名。</summary>
        public const string ManifestFileName = "attachments.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>
        /// 定位带附件配置的角色包目录：遍历 Characters 下各角色包，返回第一个含 attachments.json 的。
        /// </summary>
        public static string? FindPackageWithAttachments()
        {
            var charactersDir = PromptService.FindCharactersDirectory();
            if (charactersDir == null) return null;

            foreach (var packageDir in Directory.GetDirectories(charactersDir))
            {
                if (File.Exists(Path.Combine(packageDir, ManifestFileName)))
                {
                    return packageDir;
                }
            }

            return null;
        }

        /// <summary>
        /// 读取角色包的附件配置并挂载到骨骼上。返回成功挂载的附件数量。
        /// 同名附件会先移除再挂载，保证重复调用不会叠加。
        /// </summary>
        public static int Load(SkeletonService skeletonService, string packageDir)
        {
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Warning($"[附件] 未找到 {ManifestFileName}：{packageDir}");
                return 0;
            }

            AttachmentManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<AttachmentManifest>(
                    File.ReadAllText(manifestPath), JsonOptions);
            }
            catch (Exception ex)
            {
                Log.Warning($"[附件] {ManifestFileName} 解析失败：{ex.Message}");
                return 0;
            }

            if (manifest?.Attachments == null || manifest.Attachments.Count == 0)
            {
                Log.Warning($"[附件] {ManifestFileName} 中没有附件定义");
                return 0;
            }

            // 窗口与骨骼统一使用 DIP，配置里的缩放与偏移可直接使用，无需做 DPI 换算
            var scale = manifest.Scale > 0 ? manifest.Scale : 1.0;
            var loaded = 0;

            foreach (var definition in manifest.Attachments)
            {
                if (string.IsNullOrWhiteSpace(definition.BoneId) ||
                    string.IsNullOrWhiteSpace(definition.Resource))
                {
                    Log.Warning($"[附件] 跳过缺少 boneId 或 resource 的定义：{definition.Id}");
                    continue;
                }

                // 相对路径按角色包目录解析为绝对路径；渲染层对绝对路径直接放行
                var resourcePath = Path.IsPathRooted(definition.Resource)
                    ? definition.Resource
                    : Path.Combine(packageDir, definition.Resource);

                if (!File.Exists(resourcePath))
                {
                    Log.Warning($"[附件] 图片不存在，已跳过：{resourcePath}");
                    continue;
                }

                // 重复加载时先移除同名附件，避免叠加
                if (skeletonService.GetAttachment(definition.Id) != null)
                {
                    skeletonService.RemoveAttachment(definition.Id);
                }

                var attachment = skeletonService.AddAttachment(
                    definition.BoneId, definition.Id, definition.Name, resourcePath,
                    SafeValue(definition.Offset, 0), SafeValue(definition.Offset, 1),
                    SafeValue(definition.Pivot, 0, 0.5), SafeValue(definition.Pivot, 1, 0.5),
                    definition.ZOrder,
                    scale, scale);

                if (attachment == null) continue;

                if (!definition.Visible)
                {
                    skeletonService.SetAttachmentVisible(definition.Id, false);
                }

                loaded++;
            }

            Log.Info($"[附件] 已挂载 {loaded} 个部位（缩放 {scale:F3}）：{Path.GetFileName(packageDir)}");
            return loaded;
        }

        /// <summary>
        /// 把当前骨骼上的附件状态导出回角色包的 attachments.json。
        /// 当前没有任何附件时不写盘，避免把已有配置清空。
        /// </summary>
        public static bool Save(SkeletonService skeletonService, string packageDir)
        {
            var skeleton = skeletonService.Skeleton;
            if (skeleton == null) return false;

            var definitions = new List<AttachmentDefinition>();
            var scale = 1.0;

            foreach (var bone in skeleton.Bones)
            {
                foreach (var attachment in bone.Attachments)
                {
                    // 载入时整套图共用同一个缩放系数，取首个附件即可还原
                    if (definitions.Count == 0) scale = attachment.ScaleX;

                    definitions.Add(new AttachmentDefinition
                    {
                        Id = attachment.Id,
                        BoneId = attachment.BoneId,
                        Name = attachment.Name,
                        Resource = ToResourcePath(packageDir, attachment.GetCurrentResourcePath()),
                        Pivot = new[] { attachment.Pivot.X, attachment.Pivot.Y },
                        Offset = new[] { attachment.Offset.X, attachment.Offset.Y },
                        ZOrder = attachment.ZOrder,
                        Visible = attachment.Visible
                    });
                }
            }

            if (definitions.Count == 0)
            {
                Log.Warning("[附件] 当前没有挂载任何附件，跳过导出");
                return false;
            }

            if (scale <= 0) scale = 1.0;

            var manifest = new AttachmentManifest
            {
                Version = ReadVersion(packageDir),
                Scale = scale,
                Attachments = definitions
            };

            try
            {
                File.WriteAllText(
                    Path.Combine(packageDir, ManifestFileName),
                    JsonSerializer.Serialize(manifest, JsonOptions));
                Log.Info($"[附件] 已导出 {definitions.Count} 个部位：{Path.GetFileName(packageDir)}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[附件] 导出失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>读取配件自身坐标时按索引取值，数组缺失或长度不足时退回缺省值。</summary>
        private static double SafeValue(double[]? values, int index, double fallback = 0.0)
        {
            return values != null && index < values.Length ? values[index] : fallback;
        }

        /// <summary>
        /// 把图片绝对路径转回相对角色包目录的写法；不在包内时保留绝对路径。
        /// 统一使用正斜杠，避免配置在跨平台分发时出现反斜杠。
        /// </summary>
        private static string ToResourcePath(string packageDir, string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            var relative = Path.GetRelativePath(packageDir, path);
            var result = relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
            return result.Replace('\\', '/');
        }

        /// <summary>沿用配置中原有的版本号；文件缺失或读取失败时返回空串。</summary>
        private static string ReadVersion(string packageDir)
        {
            try
            {
                var manifestPath = Path.Combine(packageDir, ManifestFileName);
                if (!File.Exists(manifestPath)) return string.Empty;

                var manifest = JsonSerializer.Deserialize<AttachmentManifest>(
                    File.ReadAllText(manifestPath), JsonOptions);
                return manifest?.Version ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
