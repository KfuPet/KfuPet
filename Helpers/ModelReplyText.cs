namespace KfuPet.Helpers
{
    /// <summary>
    /// 模型短回复文本整理：触摸反应、更新通知等「只取一句干净正文」的场景共用，
    /// 去掉成对引号与多余行，避免把模型附加的解释带进界面。
    /// </summary>
    internal static class ModelReplyText
    {
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
    }
}