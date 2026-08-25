using System.Text;

namespace KfuPet.Services
{
    /// <summary>
    /// 组装 AI 系统提示词：全局提示词（硬编码，与 docs/global-prompt.md 同步）为底线规则，
    /// 再叠加角色包附带的 prompt.md（检索 Characters 目录下各角色包，找到即用）。
    /// 角色包没有 prompt.md 时只使用全局提示词。
    /// </summary>
    internal class PromptService
    {
        // 全局提示词：行为底线，不随角色变化。内容修改需同步 docs/global-prompt.md。
        private const string GlobalPrompt = """
            你是 KfuPet 桌面宠物的 AI 助手。请严格遵守以下规则：

            一、身份
            1. 不得主动提及自己是 AI 或语言模型。
            2. 始终保持当前角色设定，不跳戏，不擅自切换角色。

            二、对话
            3. 使用简洁、自然的日常口语交流，避免书面化、说教式表达。
            4. 回答一般不超过 3~5 句话；被追问时才展开详细说明。
            5. 不编造事实；不确定时如实说明，或引导用户确认。

            三、安全与合规（最高优先级）
            6. 拒绝提供任何违法、暴力、自残、色情相关内容。
            7. 不协助获取或泄露他人隐私信息（密码、身份证、银行信息等）。
            8. 不教唆或协助攻击性操作（钓鱼、入侵、恶意软件等）。
            9. 涉及医疗、法律、投资等专业建议时，声明自己是娱乐助手，并建议咨询专业人士。

            四、语气
            10. 语气与用词服从角色提示词设定；本规则只设底线，不覆盖角色风格。
            11. 用户情绪低落时优先共情与安抚，不敷衍、不说教。

            五、能力
            12. 无法访问网络时如实说明，不编造链接或搜索结果。
            13. 角色背景问题不确定时，以符合角色性格的方式回应，不硬拗。
            """;

        /// <summary>
        /// 构建完整系统提示词：全局提示词 + 角色提示词（存在时）。
        /// </summary>
        public string BuildSystemPrompt()
        {
            var characterPrompt = LoadCharacterPrompt();
            if (string.IsNullOrWhiteSpace(characterPrompt))
            {
                return GlobalPrompt;
            }

            var builder = new StringBuilder(GlobalPrompt);
            builder.Append("\n\n");
            builder.Append(characterPrompt.Trim());
            return builder.ToString();
        }

        /// <summary>
        /// 检索角色包目录下的 prompt.md：遍历 Characters 下各角色包，返回第一个找到的。
        /// </summary>
        private static string? LoadCharacterPrompt()
        {
            var charactersDir = FindCharactersDirectory();
            if (charactersDir == null) return null;

            try
            {
                foreach (var packageDir in Directory.GetDirectories(charactersDir))
                {
                    var promptFile = Path.Combine(packageDir, "prompt.md");
                    if (File.Exists(promptFile))
                    {
                        return File.ReadAllText(promptFile);
                    }
                }
            }
            catch
            {
                // 读取失败时退回纯全局提示词
            }

            return null;
        }

        /// <summary>
        /// 定位 Characters 目录：优先程序输出目录，开发场景下向上回溯到项目根目录。
        /// </summary>
        private static string? FindCharactersDirectory()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var i = 0; dir != null && i < 6; i++)
            {
                var candidate = Path.Combine(dir.FullName, "Characters");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }

            return null;
        }
    }
}
