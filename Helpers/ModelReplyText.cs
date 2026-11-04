using System.Text.RegularExpressions;

namespace KfuPet.Helpers
{
    /// <summary>
    /// 模型短回复文本整理：触摸反应、更新通知等「只取一句干净正文」的场景共用，
    /// 去掉成对引号与多余行，避免把模型附加的解释带进界面。
    /// </summary>
    internal static class ModelReplyText
    {
        /// <summary>裸网址结尾可能跟随的标点：删除网址时保留这些标点，避免句子被截断。</summary>
        private const string UrlTrailingPunctuation = ".,;:!?)]}，。；：！？）】》";

        /// <summary>Markdown 链接与图片：保留显示文字，去掉网址。</summary>
        private static readonly Regex MarkdownLinkPattern = new(
            @"!?\[([^\]]*)\]\((?:[^()]|\([^()]*\))*\)", RegexOptions.Compiled);

        /// <summary>尖括号自动链接，例如 &lt;https://example.com&gt;。</summary>
        private static readonly Regex AngleLinkPattern = new(
            @"<https?://[^>\s]+>", RegexOptions.Compiled);

        /// <summary>裸网址（含 www. 开头）：只匹配 URL 合法 ASCII 字符，避免把后面的中文正文一起吞掉。</summary>
        private static readonly Regex BareUrlPattern = new(
            @"(?:https?://|www\.)[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+", RegexOptions.Compiled);

        /// <summary>删除链接后留下的空括号。</summary>
        private static readonly Regex EmptyBracketPattern = new(
            @"[（(]\s*[)）]", RegexOptions.Compiled);

        /// <summary>连续的水平空白。</summary>
        private static readonly Regex HorizontalSpacePattern = new(
            @"[ \t]{2,}", RegexOptions.Compiled);

        /// <summary>标点前的多余空格。</summary>
        private static readonly Regex SpaceBeforePunctuationPattern = new(
            @"[ \t]+([，。！？；：、）】》])", RegexOptions.Compiled);

        /// <summary>
        /// 整理成单行正文：去首尾空白、去成对引号（""、「」、『』），只保留第一行。
        /// </summary>
        public static string ToSingleLine(string reply)
        {
            var text = reply.Trim();

            // 去掉模型自行添加的成对引号
            if (text.Length >= 2)
            {
                var first = text[0];
                var last = text[^1];
                if ((first == '"' && last == '"') ||
                    (first == '「' && last == '」') ||
                    (first == '『' && last == '』'))
                {
                    text = text[1..^1].Trim();
                }
            }

            // 只保留第一行，避免模型在正文后追加解释
            var newlineIndex = text.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex > 0)
            {
                text = text[..newlineIndex].Trim();
            }

            return text;
        }

        /// <summary>
        /// 去掉文本中的链接：Markdown 链接保留显示文字，裸网址与尖括号链接直接移除。
        /// 用于所有面向用户的回复与写入记忆的内容，避免链接进入气泡或记忆文件。
        /// </summary>
        public static string StripLinks(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var result = MarkdownLinkPattern.Replace(text, "$1");
            result = AngleLinkPattern.Replace(result, string.Empty);
            result = BareUrlPattern.Replace(result, KeepTrailingPunctuation);

            // 清理删除链接后留下的空括号与多余空白
            result = EmptyBracketPattern.Replace(result, string.Empty);
            result = HorizontalSpacePattern.Replace(result, " ");
            result = SpaceBeforePunctuationPattern.Replace(result, "$1");

            return result.Trim();
        }

        /// <summary>删除网址本身，但把紧随网址的标点保留下来。</summary>
        private static string KeepTrailingPunctuation(Match match)
        {
            var url = match.Value;
            var end = url.Length;
            while (end > 0 && UrlTrailingPunctuation.IndexOf(url[end - 1]) >= 0)
            {
                end--;
            }

            return end < url.Length ? url[end..] : string.Empty;
        }
    }
}