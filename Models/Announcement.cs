namespace KfuPet.Models
{
    /// <summary>
    /// 一条官方公告，来自公告仓库（KfuPet/.github）的 announcements.json。
    /// </summary>
    public class Announcement
    {
        /// <summary>公告唯一标识；同一标识已展示过时不再重复弹窗。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>发布日期（建议 yyyy-MM-dd，用于从列表中取最新一条）。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>公告级别：info（默认）、warning、important。</summary>
        public string Level { get; set; } = "info";

        /// <summary>公告标题。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>公告正文（Markdown）。</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>可选详情链接，弹窗中“查看详情”按钮打开。</summary>
        public string Link { get; set; } = string.Empty;
    }
}
