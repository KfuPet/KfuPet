using System.Text;
using KfuPet.Helpers;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 更新通知文案服务：把「发现新版本」整理成发给 AI 的事件提示（<see cref="BuildEventPrompt"/>），
    /// 由 AI 按角色人设写一句通知正文；未接入 AI 或请求失败时由调用方回退到固定文案。
    /// </summary>
    internal static class UpdateNotificationService
    {
        /// <summary>更新说明进入提示词的最大长度（字符）：超出截断，避免单条通知消耗过多 token。</summary>
        private const int ReleaseNotesExcerptLength = 400;

        /// <summary>提示词中要求 AI 控制的通知正文字数（软约束）。</summary>
        private const int RequestedNotificationLength = 40;

        /// <summary>通知正文的硬上限（字符）：模型未按要求输出时截断，避免气泡通知文本超长。</summary>
        private const int MaxNotificationLength = 100;

        /// <summary>
        /// 构造更新事件给 AI 的用户消息：说明新版本与更新内容，并要求一句符合人设的通知正文。
        /// </summary>
        public static string BuildEventPrompt(UpdateCheckResult result)
        {
            var notes = (result.ReleaseNotes ?? string.Empty).Trim();
            if (notes.Length > ReleaseNotesExcerptLength)
            {
                notes = notes[..ReleaseNotesExcerptLength];
            }

            var builder = new StringBuilder();
            builder.Append($"（系统事件）你的新版本 v{result.LatestVersion.ToString(3)} 已经发布");
            builder.Append($"，主人当前使用的是 v{result.CurrentVersion.ToString(3)}。");
            if (notes.Length > 0)
            {
                builder.Append($"\n本次版本的更新说明（节选）：\n{notes}\n");
            }

            builder.Append("请用符合你人设的一句话告诉主人有新版本可以更新，并提醒主人点击这条通知查看更新详情" +
                           $"（{RequestedNotificationLength} 字以内）。只输出这一句话：不要引号、不要旁白、不要解释。");
            return builder.ToString();
        }

        /// <summary>
        /// 整理 AI 回复为一句通知正文：去掉引号与多余行，超出硬上限时截断。
        /// </summary>
        public static string NormalizeLine(string reply)
        {
            var text = ModelReplyText.ToSingleLine(reply);
            if (text.Length > MaxNotificationLength)
            {
                text = text[..MaxNotificationLength] + "…";
            }

            return text;
        }
    }
}