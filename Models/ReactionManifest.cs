namespace KfuPet.Models
{
    /// <summary>
    /// reactions.json（触摸反应台词配置）的数据契约，描述各部位被触摸时说的话。
    /// 与 character.json、attachments.json 同处角色包目录下，属于可选配置。
    /// </summary>
    public class ReactionManifest
    {
        /// <summary>配置版本号。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 触摸事件提示（发给 AI 的"发生了什么"描述与应答要求），键：
        /// headPet（抚摸头部）/ head / armLeft / armRight / legLeft / legRight / body / instruction（应答要求）。
        /// 缺省项用程序内置默认文案，整段可以省略。
        /// </summary>
        public Dictionary<string, string>? EventPrompt { get; set; }

        /// <summary>
        /// 各部位的反应台词，键为部位键（head / armLeft / armRight / legLeft / legRight / body），
        /// 值为该部位的候选台词列表（随机抽取）。左右与骨骼命名一致，指画面上的左右。
        /// </summary>
        public Dictionary<string, List<string>> Reactions { get; set; } = new Dictionary<string, List<string>>();
    }
}