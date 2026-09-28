using System.Windows.Media.Imaging;

namespace KfuPet.Models
{
    /// <summary>
    /// 角色模型窗口展示用的角色包信息：角色包清单内容 + 解析好的图片资源。
    /// </summary>
    public sealed class CharacterPackageInfo
    {
        /// <summary>角色名称；无效角色包显示文件夹名。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>角色包版本号。</summary>
        public string Version { get; init; } = string.Empty;

        /// <summary>作者名称。</summary>
        public string Author { get; init; } = string.Empty;

        /// <summary>角色描述。</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>标签集合。</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        /// <summary>角色包目录（绝对路径）。</summary>
        public string DirectoryPath { get; init; } = string.Empty;

        /// <summary>预览图片；缺失或加载失败时为 null，界面显示占位图。</summary>
        public BitmapImage? PreviewImage { get; init; }

        /// <summary>作者头像；缺失或加载失败时为 null，界面显示作者名首字占位。</summary>
        public BitmapImage? AuthorAvatar { get; init; }

        /// <summary>是否为有效角色包（character.json 存在且可解析）。</summary>
        public bool IsValid { get; init; } = true;

        /// <summary>无效原因说明，仅无效角色包使用。</summary>
        public string InvalidReason { get; init; } = string.Empty;

        /// <summary>作者头像缺省占位文字：作者名首字。</summary>
        public string AuthorInitial =>
            string.IsNullOrWhiteSpace(Author) ? "?" : Author.Trim().Substring(0, 1);

        /// <summary>标签的单行展示文本（" · " 分隔）。</summary>
        public string TagsText => string.Join(" · ", Tags);

        /// <summary>版本号展示文本（形如 "v1.0.0"；未填写版本时为空串）。</summary>
        public string VersionText => string.IsNullOrWhiteSpace(Version) ? string.Empty : $"v{Version}";
    }
}