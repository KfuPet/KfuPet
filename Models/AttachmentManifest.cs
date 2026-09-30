namespace KfuPet.Models
{
    /// <summary>
    /// attachments.json（附件配置）的数据契约，描述角色包的部位图片与骨骼的绑定关系。
    /// 与 character.json 同处角色包目录下，图片资源放在 images/ 中。
    /// </summary>
    public class AttachmentManifest
    {
        /// <summary>配置版本号。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 整套部位图的统一缩放系数：把原始画布像素换算为窗口 DIP。
        /// 部位图同源等比裁切时只需这一个系数，避免逐张重复填写。
        /// </summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>附件定义集合。</summary>
        public List<AttachmentDefinition> Attachments { get; set; } = new List<AttachmentDefinition>();
    }
}
