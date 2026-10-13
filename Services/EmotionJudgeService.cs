using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>对话后情绪判断的结果：三个维度的增减量。</summary>
    internal readonly record struct EmotionJudgeResult(double Joy, double Energy, double Intimacy)
    {
        /// <summary>三个维度是否都可视为没有变化（小于半格）。</summary>
        public bool IsNoChange => Math.Abs(Joy) < 0.5 && Math.Abs(Energy) < 0.5 && Math.Abs(Intimacy) < 0.5;
    }

    /// <summary>
    /// 对话后的情绪判断：主人说完一轮话之后，单独发一次请求，让模型判断这句话里流露出的态度
    /// （夸奖 / 责备 / 关心 / 冷淡 / 与态度无关），并折算成情绪增减量。
    /// 与主对话里的 emotion_adjust 工具互补——工具负责本轮语气一致，本服务负责保证每一轮都判过，
    /// 因此模型的工具调用能力不可用（自动降级为普通对话）时，情绪仍有模型侧来源。
    /// </summary>
    internal static class EmotionJudgeService
    {
        /// <summary>判断结果里每个维度的最大幅度，与 emotion_adjust 工具的单次钳制保持一致。</summary>
        private const double MaxDelta = 15;

        /// <summary>主人回复过长时截断的字符数，避免判断请求本身消耗过多 token。</summary>
        private const int MaxReplyChars = 300;

        /// <summary>构造判断用的消息列表：一条约束输出格式的系统提示，加一条带上下文的分析请求。</summary>
        /// <param name="userMessage">主人这一轮说的话。</param>
        /// <param name="assistantReply">桌宠这一轮的回复，用于提供判断语气的上下文。</param>
        public static IReadOnlyList<ChatMessage> BuildMessages(string userMessage, string assistantReply)
        {
            const string system = """
                你是一个情绪分析器，负责判断「主人」对桌宠角色的态度。
                只输出一行 JSON，不要解释、不要 Markdown 代码块、不要多余文字。
                格式：{"joy": 0, "energy": 0, "intimacy": 0}
                规则：
                - 三个值都是 -15 到 15 之间的整数。
                - 主人夸奖、关心、亲近、撒娇 → 正数；责备、嫌弃、冷淡、发火 → 负数。
                - 普通提问、求助、闲聊、与态度无关的内容 → 三个值全部为 0。
                - 只在态度确实明显时才给非零值，拿不准就全部输出 0。
                """;

            var reply = assistantReply.Length > MaxReplyChars
                ? assistantReply[..MaxReplyChars]
                : assistantReply;

            var request = $"""
                【主人说的话】{userMessage}
                【桌宠的回复】{reply}
                请判断主人这句话里对桌宠的态度，按上面的格式输出 JSON。
                """;

            return new List<ChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = request }
            };
        }

        /// <summary>解析模型返回的 JSON；格式不对时返回 false，由调用方按“无变化”处理。</summary>
        /// <param name="reply">模型返回的原始文本。</param>
        /// <param name="result">解析出的增减量。</param>
        public static bool TryParse(string? reply, out EmotionJudgeResult result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(reply))
            {
                return false;
            }

            // 模型有时会把 JSON 包在代码块或解释文字里，这里只取最外层的一对花括号
            var start = reply.IndexOf('{');
            var end = reply.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(reply.Substring(start, end - start + 1));
                var root = document.RootElement;
                result = new EmotionJudgeResult(
                    ReadNumber(root, "joy"),
                    ReadNumber(root, "energy"),
                    ReadNumber(root, "intimacy"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读取一个维度的增减量，缺失或超范围时按 0 / 钳制处理。</summary>
        private static double ReadNumber(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
            {
                return 0;
            }

            return Math.Clamp(element.GetDouble(), -MaxDelta, MaxDelta);
        }
    }
}
