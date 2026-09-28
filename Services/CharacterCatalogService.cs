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
                foreach (var packageDir in Directory.GetDirectories(charactersDir))
                {
                    packages.Add(LoadPackage(packageDir));
                }

                // 有效包排前面；同组内按名称排序，保证多次刷新顺序稳定
                packages.Sort((a, b) => a.IsValid != b.IsValid
                    ? (a.IsValid ? -1 : 1)
                    : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

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

                return new CharacterPackageInfo
                {
                    Name = Clean(manifest.Name) is { Length: > 0 } name ? name : folderName,
                    Version = Clean(manifest.Version),
                    Author = Clean(manifest.Author),
                    Description = Clean(manifest.Description),
                    Tags = manifest.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>(),
                    DirectoryPath = packageDir,
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
                DirectoryPath = packageDir,
                IsValid = false,
                InvalidReason = reason
            };
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