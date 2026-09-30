namespace KfuPet.Models
{
    /// <summary>
    /// attachments.json 中单条附件定义的数据契约：把角色包里的一张部位图片绑定到某根骨骼上。
    /// 坐标与 pivot 用长度为 2 的数组表示，顺序为 [x, y]。
    /// </summary>
    public class AttachmentDefinition
    {
        /// <summary>附件唯一标识，同一角色包内不可重复。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>要挂载到的骨骼 ID。</summary>
        public string BoneId { get; set; } = string.Empty;

        /// <summary>附件显示名称，主要用于开发者工具中辨认。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>图片路径，相对角色包目录（也支持绝对路径），如 images/head.png。</summary>
        public string Resource { get; set; } = string.Empty;

        /// <summary>旋转与定位锚点（图片自身比例，0~1）。缺省为图片中心 [0.5, 0.5]。</summary>
        public double[] Pivot { get; set; } = new[] { 0.5, 0.5 };

        /// <summary>相对骨骼锚点的偏移（DIP）。缺省 [0, 0]。</summary>
        public double[] Offset { get; set; } = new[] { 0.0, 0.0 };

        /// <summary>渲染层级，值越大越靠前。</summary>
        public int ZOrder { get; set; }

        /// <summary>是否显示，缺省显示。</summary>
        public bool Visible { get; set; } = true;
    }
}
