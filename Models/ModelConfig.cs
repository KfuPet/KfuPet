namespace KfuPet.Models
{
    /// <summary>
    /// 一条 AI 模型配置，对应设置界面“模型”页里的一张卡片。
    /// </summary>
    public class ModelConfig
    {
        /// <summary>唯一标识，用于在本地配置中定位并作为列表项的关联键。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>服务商名称。</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>API 基础地址。</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>模型名称。</summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>是否当前正在使用的模型（同时最多一个生效）。</summary>
        public bool IsActive { get; set; }
    }
}
