using System.Text.Json;
using System.Windows.Media.Imaging;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 角色模型目录扫描服务：遍历 Characters 目录下各角色包，读取 character.json，
    /// 汇总为角色模型窗口展示所需的信息列表（含预览图与作者头像）。
    /// </summary>
    internal class CharacterCatalogService
    {
        /// <summary>角色包清单文件名。</summary>
        public const string ManifestFileName = "character.json";

        /// <summary>删除流程的临时目录后缀：先改名到此后缀再删，避免删除中途失败留下“半删”的角色包；各处扫描都会跳过此后缀。</summary>
        public const string TemporaryDeleteSuffix = ".deleting";

        /// <summary>预览图解码宽度上限，避免原图过大占用内存。</summary>
        private const int PreviewDecodeWidth = 480;

        /// <summary>作者头像解码宽度上限。</summary>
        private const int AvatarDecodeWidth = 96;

        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// 扫描 Characters 目录并加载全部角色包；图片解码在后台线程完成（冻结后跨线程使用），不阻塞界面。
        /// </summary>
        public Task<IReadOnlyList<CharacterPackageInfo>> LoadPackagesAsync()
        {
            return Task.Run<IReadOnlyList<CharacterPackageInfo>>(() =>
            {
                var charactersDir = PromptService.FindCharactersDirectory();
                if (charactersDir == null)
                {
                    Log.Warning("[角色] 未找到 Characters 目录，模型列表为空");
                    return new List<CharacterPackageInfo>();
                }

                var packages = new List<CharacterPackageInfo>();
                foreach (var packageDir in GetCharacterPackageDirectories())
                {
                    packages.Add(LoadPackage(packageDir));
                }

                // 有效包排前面；同组内按名称排序，保证多次刷新顺序稳定
                packages.Sort((a, b) => a.IsValid != b.IsValid
                    ? (a.IsValid ? -1 : 1)
                    : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

                // 当前使用的角色包（设置里记录，未记录时按默认角色）与删除限制一次标好：
                // 正在使用的角色包不允许删除；目录里只剩最后一个角色包时也不允许删除
                var activeDirectory = ResolveActivePackageDirectory();
                foreach (var package in packages)
                {
                    package.IsActiveCharacter = activeDirectory != null &&
                        string.Equals(package.DirectoryPath, activeDirectory, StringComparison.OrdinalIgnoreCase);
                    package.IsDeletable = packages.Count > 1 && !package.IsActiveCharacter;
                }

                Log.Info($"[角色] 模型扫描完成：共 {packages.Count} 个（有效 {packages.Count(p => p.IsValid)} 个）");
                return packages;
            });
        }

        /// <summary>读取单个角色包；缺清单或解析失败时返回带原因的无效占位信息。</summary>
        private static CharacterPackageInfo LoadPackage(string packageDir)
        {
            var folderName = Path.GetFileName(packageDir);
            var manifestPath = Path.Combine(packageDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return InvalidPackage(packageDir, folderName, $"缺少 {ManifestFileName}");
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<CharacterManifest>(
                    File.ReadAllText(manifestPath), ManifestJsonOptions);
                if (manifest == null)
                {
                    return InvalidPackage(packageDir, folderName, $"{ManifestFileName} 内容为空");
                }

                var (isComplete, incompleteReason) = CheckCompleteness(packageDir);

                return new CharacterPackageInfo
                {
                    Name = Clean(manifest.Name) is { Length: > 0 } name ? name : folderName,
                    Id = Clean(manifest.Id) is { Length: > 0 } id ? id : folderName,
                    Version = Clean(manifest.Version),
                    Author = Clean(manifest.Author),
                    Description = Clean(manifest.Description),
                    Tags = manifest.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>(),
                    DirectoryPath = packageDir,
                    IsComplete = isComplete,
                    IncompleteReason = incompleteReason,
                    PreviewImage = LoadImage(packageDir, manifest.Preview, PreviewDecodeWidth),
                    AuthorAvatar = LoadImage(packageDir, manifest.AuthorAvatar, AvatarDecodeWidth)
                };
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色包 {folderName} 加载失败：{ex.Message}");
                return InvalidPackage(packageDir, folderName, $"{ManifestFileName} 解析失败");
            }
        }

        private static CharacterPackageInfo InvalidPackage(string packageDir, string folderName, string reason)
        {
            Log.Warning($"[角色] 无效角色包 {folderName}：{reason}");
            return new CharacterPackageInfo
            {
                Name = folderName,
                Id = folderName,
                DirectoryPath = packageDir,
                IsValid = false,
                IsComplete = false,
                IncompleteReason = $"缺少 {ManifestFileName}",
                InvalidReason = reason
            };
        }

        /// <summary>
        /// 角色包完整性判定：外层已确认 character.json 可用，这里再要求 prompt.md、attachments.json
        /// 与 reactions.json 的必填项缺一不可；返回是否完整与缺失项说明。
        /// </summary>
        private static (bool IsComplete, string Reason) CheckCompleteness(string packageDir)
        {
            var missing = new List<string>();

            if (!File.Exists(Path.Combine(packageDir, PromptService.PromptFileName)))
            {
                missing.Add(PromptService.PromptFileName);
            }

            if (!File.Exists(Path.Combine(packageDir, CharacterAttachmentService.ManifestFileName)))
            {
                missing.Add(CharacterAttachmentService.ManifestFileName);
            }

            var missingReactionKeys = TouchReactionService.FindMissingReactionKeys(packageDir);
            if (missingReactionKeys.Count > 0)
            {
                missing.Add($"reactions.json 的 {missingReactionKeys.Count} 项必填内容");
            }

            return missing.Count == 0
                ? (true, string.Empty)
                : (false, "缺少 " + string.Join("、", missing));
        }

        /// <summary>是否为删除流程留下的临时目录（改名后尚未删净的残留）。</summary>
        public static bool IsTemporaryDeleteDirectory(string packageDir) =>
            packageDir.EndsWith(TemporaryDeleteSuffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 列出 Characters 目录下的角色包目录，并过滤掉删除流程的临时目录；
        /// 角色包检索、附件挂载、当前角色解析与角色模型列表共用此入口。
        /// </summary>
        public static string[] GetCharacterPackageDirectories()
        {
            var charactersDir = PromptService.FindCharactersDirectory();
            if (charactersDir == null) return Array.Empty<string>();

            try
            {
                return Directory.GetDirectories(charactersDir)
                    .Where(directory => !IsTemporaryDeleteDirectory(directory))
                    .ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色包目录读取失败：{ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 解析当前生效的角色包目录：优先设置里记录的角色包，其次第一个含 attachments.json 的角色包，
        /// 最后第一个含 character.json 的角色包；都找不到时返回 null。
        /// 启动时挂载附件、构建提示词与角色模型窗口判定「使用中」共用此入口。
        /// </summary>
        public static string? ResolveActivePackageDirectory()
        {
            var packageDirs = GetCharacterPackageDirectories();
            if (packageDirs.Length == 0) return null;

            // 优先：设置里记录的角色包（角色模型窗口「使用」写入）
            var activeId = SettingsService.Instance.ActiveCharacterId;
            if (activeId.Length > 0)
            {
                foreach (var packageDir in packageDirs)
                {
                    if (string.Equals(ReadCharacterId(packageDir), activeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return packageDir;
                    }
                }

                Log.Warning($"[角色] 设置中记录的角色包已不存在，回退到默认角色：{activeId}");
            }

            // 回退：第一个带部位图配置的角色包，其次第一个有 character.json 的角色包
            var withAttachments = CharacterAttachmentService.FindPackageWithAttachments();
            if (withAttachments != null) return withAttachments;

            foreach (var packageDir in packageDirs)
            {
                if (File.Exists(Path.Combine(packageDir, ManifestFileName)))
                {
                    return packageDir;
                }
            }

            return null;
        }

        /// <summary>读取角色包标识：character.json 的 id，缺失、解析失败或缺省时用文件夹名。</summary>
        public static string ReadCharacterId(string packageDir)
        {
            var folderName = Path.GetFileName(packageDir);
            try
            {
                var manifestPath = Path.Combine(packageDir, ManifestFileName);
                if (!File.Exists(manifestPath)) return folderName;

                var manifest = JsonSerializer.Deserialize<CharacterManifest>(
                    File.ReadAllText(manifestPath), ManifestJsonOptions);
                return Clean(manifest?.Id) is { Length: > 0 } id ? id : folderName;
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 角色包标识读取失败（{folderName}）：{ex.Message}");
                return folderName;
            }
        }

        /// <summary>
        /// 从角色包目录加载图片：路径支持相对（相对角色包目录）与绝对两种写法。
        /// 解码后立即冻结，以便在后台线程创建、界面线程直接使用。
        /// </summary>
        private static BitmapImage? LoadImage(string packageDir, string? path, int decodeWidth)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(packageDir, path.Trim());
                if (!File.Exists(fullPath)) return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(fullPath, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;   // 立即解码，不占用源文件
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = decodeWidth;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Log.Warning($"[角色] 图片加载失败（{path}）：{ex.Message}");
                return null;
            }
        }

        /// <summary>去除字符串首尾空白；JSON 中显式写了 null 时按空串处理。</summary>
        private static string Clean(string? value) => value?.Trim() ?? string.Empty;
    }
}