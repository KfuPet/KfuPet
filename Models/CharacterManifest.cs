namespace KfuPet.Models
{
    /// <summary>
    /// 角色包清单（character.json）的数据契约，描述角色包的基本信息。
    /// 图片与提示词字段填相对角色包目录的路径（也支持绝对路径）。
    /// </summary>
    public class CharacterManifest
    {
        /// <summary>角色包唯一标识（英文/数字），缺省时以文件夹名代替。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>角色名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>角色包版本号。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>作者名称。</summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>作者头像图片路径。</summary>
        public string AuthorAvatar { get; set; } = string.Empty;

        /// <summary>角色描述。</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>预览图片路径。</summary>
        public string Preview { get; set; } = string.Empty;

        /// <summary>标签集合，供搜索过滤。</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>角色提示词文件路径，缺省为 prompt.md。</summary>
        public string Prompt { get; set; } = string.Empty;
    }
}