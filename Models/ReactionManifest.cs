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
        /// 各部位的反应台词，键为部位键（head / armLeft / armRight / legLeft / legRight / body），
        /// 值为该部位的候选台词列表（随机抽取）。左右与骨骼命名一致，指画面上的左右。
        /// </summary>
        public Dictionary<string, List<string>> Reactions { get; set; } = new Dictionary<string, List<string>>();
    }
}