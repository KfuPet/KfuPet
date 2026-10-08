namespace KfuPet.Models
{
    /// <summary>
    /// reactions.json（角色反应台词配置）的数据契约，描述各部位被触摸、以及拖动越界回正时说的话。
    /// 与 character.json、attachments.json 同处角色包目录下；必须完整提供全部必填键，程序在启动时检测完整性。
    /// </summary>
    public class ReactionManifest
    {
        /// <summary>配置版本号。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 事件提示（发给 AI 的"发生了什么"描述与应答要求），键：
        /// headPet（抚摸头部）/ head / armLeft / armRight / legLeft / legRight / body / snapBack（拖动越界回正）/ instruction（应答要求）。
        /// 必须提供全部键，代码不再内置默认文案。
        /// </summary>
        public Dictionary<string, string>? EventPrompt { get; set; }

        /// <summary>
        /// 反应台词，键为部位键（head / armLeft / armRight / legLeft / legRight / body）或事件键 snapBack（拖动越界回正），
        /// 值为候选台词列表（随机抽取）。左右与骨骼命名一致，指画面上的左右。每个键至少一条非空台词。
        /// </summary>
        public Dictionary<string, List<string>> Reactions { get; set; } = new Dictionary<string, List<string>>();
    }
}