using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>
    /// 应用名称 → 可启动目标 的解析。
    /// 分两层：
    /// 1) <see cref="MatchCandidates"/>：对给定的候选集做打分匹配，供白名单模式（只在名单内匹配）
    ///    与完全访问模式（匹配全机应用池）共用；
    /// 2) <see cref="Match"/>：枚举全机应用池（注册表 App Paths + 开始菜单快捷方式）后交给上面那层。
    /// 匹配分档：精确 / 首字母缩写 / 子串 / 跳字，只有“精确”或“唯一且高置信”才直接给路径，
    /// 其余给出候选名单交由模型兜底挑选。
    /// 不新增第三方依赖：注册表用 Microsoft.Win32，开始菜单快捷方式直接以 .lnk 路径交给 ShellExecute 打开。
    /// </summary>
    internal static class AppResolveService
    {
        /// <summary>开始菜单最多扫描的快捷方式数量，避免异常环境下过慢。</summary>
        private const int MaxStartMenuShortcuts = 800;

        /// <summary>进入候选名单的最低分值，低于此分视为毫不相干。</summary>
        private const int MinGuessScore = 30;

        /// <summary>本地“敢自己开”的最低分值：只有精确（100）或首字母缩写（70）且唯一时才直接给出路径。</summary>
        private const int ConfidentScore = 70;

        /// <summary>
        /// 解析结果：
        /// <see cref="Path"/> 非空表示本地已能确定要打开的目标（精确或高置信猜测）；
        /// 否则 <see cref="Candidates"/> 为近似候选名称，交给模型兜底挑选。
        /// </summary>
        public readonly record struct MatchResult(string? Path, string? MatchedName, IReadOnlyList<string> Candidates);

        /// <summary>候选：显示名 + 可启动路径（exe 或 .lnk）。</summary>
        public readonly record struct Candidate(string Name, string Path);

        /// <summary>
        /// 完全访问模式入口：把名称解析到全机应用池。
        /// 名称本身就是一个存在的文件时直接使用。
        /// </summary>
        public static MatchResult Match(string name, int maxCandidates = 8)
        {
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return new MatchResult(null, null, Array.Empty<string>());
            }

            if (File.Exists(name))
            {
                return new MatchResult(name, name, Array.Empty<string>());
            }

            return MatchCandidates(name, CollectCandidates(), maxCandidates);
        }

        /// <summary>
        /// 对给定候选集做匹配（白名单模式传入白名单条目，完全访问模式传入全机候选池）。
        /// 只会返回候选集内的目标，绝不外扩。
        /// </summary>
        public static MatchResult MatchCandidates(string name, IReadOnlyList<Candidate> candidates, int maxCandidates = 8)
        {
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0 || Normalize(name).Length == 0 || candidates.Count == 0)
            {
                return new MatchResult(null, null, Array.Empty<string>());
            }

            var scored = new List<(string Name, string Path, int Score)>();
            foreach (var candidate in candidates)
            {
                var score = Score(name, candidate.Name);
                if (score > 0)
                {
                    scored.Add((candidate.Name, candidate.Path, score));
                }
            }

            if (scored.Count == 0)
            {
                return new MatchResult(null, null, Array.Empty<string>());
            }

            // 精确匹配优先
            var exact = scored.Where(x => x.Score >= 100).ToList();
            if (exact.Count > 0)
            {
                return new MatchResult(exact[0].Path, exact[0].Name, Array.Empty<string>());
            }

            // 高置信猜测：唯一最高分且达到 ConfidenceScore 才敢直接给路径
            var bestScore = scored.Max(x => x.Score);
            var bestList = scored.Where(x => x.Score == bestScore).ToList();
            if (bestScore >= ConfidentScore && bestList.Count == 1)
            {
                return new MatchResult(bestList[0].Path, bestList[0].Name, Array.Empty<string>());
            }

            // 猜不准：把候选交回给模型
            var suggestions = scored
                .Where(x => x.Score >= MinGuessScore)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name.Length)
                .Select(x => x.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCandidates)
                .ToList();

            return new MatchResult(null, null, suggestions);
        }

        /// <summary>
        /// 给“查询名”与“候选名”打分：100 完全匹配 / 70 首字母缩写 / 55 子串 / 35 跳字 / 0 不匹配。
        /// </summary>
        private static int Score(string queryName, string candidateName)
        {
            var query = Normalize(queryName);
            var candidate = Normalize(candidateName);
            if (query.Length == 0 || candidate.Length == 0)
            {
                return 0;
            }

            if (query == candidate)
            {
                return 100;
            }

            // 首字母缩写：Visual Studio Code → vsc
            if (Initials(candidateName) == query)
            {
                return 70;
            }

            // 子串：vscode 含 code / 双向都试，过短的候选不参与以免误判
            if (Math.Min(query.Length, candidate.Length) >= 3 &&
                (candidate.Contains(query, StringComparison.Ordinal) || query.Contains(candidate, StringComparison.Ordinal)))
            {
                return 55;
            }

            // 跳字：vscode 是 visualstudiocode 的子序列
            if (query.Length >= 3 && IsSubsequence(query, candidate))
            {
                return 35;
            }

            return 0;
        }

        /// <summary>取名字的首字母缩写（按非字母数字字符分词），例如 “Visual Studio Code” → “vsc”。</summary>
        private static string Initials(string text)
        {
            var builder = new System.Text.StringBuilder();
            var atWordStart = true;

            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (atWordStart)
                    {
                        builder.Append(char.ToLowerInvariant(c));
                    }

                    atWordStart = false;
                }
                else
                {
                    atWordStart = true;
                }
            }

            return builder.ToString();
        }

        /// <summary>needle 的字符是否按顺序出现在 haystack 中（跳字匹配）。</summary>
        private static bool IsSubsequence(string needle, string haystack)
        {
            var index = 0;
            foreach (var c in haystack)
            {
                if (index < needle.Length && needle[index] == c)
                {
                    index++;
                }

                if (index == needle.Length)
                {
                    return true;
                }
            }

            return index == needle.Length;
        }

        /// <summary>归一化：只保留字母数字与中日韩文字并转小写，用于忽略空格、符号、大小写的比较。</summary>
        private static string Normalize(string text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        /// <summary>收集全机候选：注册表 App Paths 条目 + 开始菜单快捷方式。</summary>
        private static List<Candidate> CollectCandidates()
        {
            var candidates = CollectAppPathCandidates();
            var appPathCount = candidates.Count;
            candidates.AddRange(CollectStartMenuCandidates());

            Log.Debug($"[打开应用] 候选池：App Paths {appPathCount} 条，开始菜单 {candidates.Count - appPathCount} 条");
            return candidates;
        }

        /// <summary>注册表 App Paths：HKLM / HKCU（含 32 位 WOW6432Node 视图）下的全部条目。</summary>
        private static List<Candidate> CollectAppPathCandidates()
        {
            var list = new List<Candidate>();
            var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };

            foreach (var prefix in new[] { string.Empty, @"WOW6432Node\" })
            {
                var rootPath = @"SOFTWARE\" + prefix + @"Microsoft\Windows\CurrentVersion\App Paths";
                foreach (var hive in hives)
                {
                    try
                    {
                        using var root = hive.OpenSubKey(rootPath);
                        if (root == null)
                        {
                            continue;
                        }

                        foreach (var subKey in root.GetSubKeyNames())
                        {
                            using var appKey = root.OpenSubKey(subKey);
                            if (appKey?.GetValue(null) is string path && path.Length > 0 && File.Exists(path))
                            {
                                list.Add(new Candidate(Path.GetFileNameWithoutExtension(subKey), path));
                            }
                        }
                    }
                    catch
                    {
                        // 注册表读取异常不影响其它来源
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 开始菜单（用户 + 公共）里的 .lnk，名称取快捷方式文件名，路径直接给 .lnk。
        /// 采用逐目录递归 + 单目录容错：某个子目录无权限或枚举失败时跳过它，不影响其它目录，
        /// 避免整轮扫描因一处异常而全部落空。
        /// </summary>
        private static List<Candidate> CollectStartMenuCandidates()
        {
            var list = new List<Candidate>();
            var scanned = 0;

            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                     })
            {
                CollectShortcutsRecursive(root, list, ref scanned);
            }

            return list;
        }

        /// <summary>递归收集一个目录下的 .lnk；文件枚举与子目录枚举各自容错。</summary>
        private static void CollectShortcutsRecursive(string directory, List<Candidate> list, ref int scanned)
        {
            if (scanned >= MaxStartMenuShortcuts || string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.GetFiles(directory, "*.lnk"))
                {
                    if (scanned++ >= MaxStartMenuShortcuts)
                    {
                        return;
                    }

                    list.Add(new Candidate(Path.GetFileNameWithoutExtension(file), file));
                }
            }
            catch
            {
                // 单个目录的文件枚举失败不影响其它目录
            }

            try
            {
                foreach (var subDirectory in Directory.GetDirectories(directory))
                {
                    if (scanned >= MaxStartMenuShortcuts)
                    {
                        return;
                    }

                    CollectShortcutsRecursive(subDirectory, list, ref scanned);
                }
            }
            catch
            {
                // 单个目录的子目录枚举失败不影响其它目录
            }
        }
    }
}
