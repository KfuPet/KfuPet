using Microsoft.Win32;

namespace KfuPet.Services
{
    /// <summary>应用名称 → 可启动目标 的解析，分两层：<see cref="MatchCandidates"/> 对候选集打分匹配（白名单与全机应用池共用）；
    /// <see cref="Match"/> 枚举全机应用池（注册表 App Paths、卸载登记项、开始菜单与桌面快捷方式、用户级安装目录）后交给前者。
    /// 匹配分档为精确/英文缩写/拼音首字母/拼音前缀/子串/跳字，只有“精确”或“唯一且高置信”才直接给路径，其余给候选名单交模型兜底。
    /// 全机候选池进程内缓存一次，避免每次请求都重扫；不新增第三方依赖。</summary>
    internal static class AppResolveService
    {
        /// <summary>开始菜单最多扫描的快捷方式数量，避免异常环境下过慢。</summary>
        private const int MaxStartMenuShortcuts = 1500;

        /// <summary>用户级安装目录最多扫描的可执行文件数量。</summary>
        private const int MaxLocalProgramExecutables = 400;

        /// <summary>用户级安装目录的最大递归深度。</summary>
        private const int MaxLocalProgramDepth = 3;

        /// <summary>进入候选名单的最低分值，低于此分视为毫不相干。</summary>
        private const int MinGuessScore = 30;

        /// <summary>本地“敢自己开”的最低分值：只有精确（100）或首字母缩写（70）且唯一时才直接给出路径。</summary>
        private const int ConfidentScore = 70;

        /// <summary>解析结果：<see cref="Path"/> 非空表示本地已确定目标（精确或高置信猜测），
        /// 否则 <see cref="Candidates"/> 为近似候选名称，交给模型兜底挑选。</summary>
        public readonly record struct MatchResult(string? Path, string? MatchedName, IReadOnlyList<string> Candidates);

        /// <summary>候选：显示名 + 可启动路径（exe 或 .lnk）+ 拼音首字母（可预填）。</summary>
        public readonly record struct Candidate(string Name, string Path, string Pinyin = "")
        {
            /// <summary>拼音首字母串：构建候选池时预填，白名单等少量条目按需计算。</summary>
            public string PinyinValue => string.IsNullOrEmpty(Pinyin) ? PinyinInitials.From(Name) : Pinyin;
        }

        /// <summary>全机候选池缓存：首次匹配时构建，之后复用。</summary>
        private static readonly object IndexGate = new();

        private static IReadOnlyList<Candidate>? _index;

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

            return MatchCandidates(name, GetIndex(), maxCandidates);
        }

        /// <summary>取全机候选池：首次调用时构建，之后直接复用缓存。</summary>
        private static IReadOnlyList<Candidate> GetIndex()
        {
            var index = _index;
            if (index != null)
            {
                return index;
            }

            lock (IndexGate)
            {
                _index ??= CollectCandidates();
                return _index;
            }
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
                var score = Score(name, candidate);
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
        /// 给“查询名”与候选打分：100 完全匹配 / 70 英文首字母或拼音首字母 / 65 拼音前缀 / 55 子串 / 35 跳字 / 0 不匹配。
        /// </summary>
        private static int Score(string queryName, Candidate candidate)
        {
            var query = Normalize(queryName);
            if (query.Length == 0)
            {
                return 0;
            }

            var candidateName = candidate.Name;
            var candidateText = Normalize(candidateName);
            if (candidateText.Length == 0)
            {
                return 0;
            }

            if (query == candidateText)
            {
                return 100;
            }

            // 英文首字母缩写：Visual Studio Code → vsc
            if (Initials(candidateName) == query)
            {
                return 70;
            }

            // 拼音首字母：微信 → wx
            var pinyin = candidate.PinyinValue;
            if (pinyin.Length > 0 && pinyin == query)
            {
                return 70;
            }

            // 拼音前缀：中文名带英文后缀时（微信 WeChat → wx）也能命中，但不足以直接打开
            if (query.Length >= 2 && pinyin.Length > 0 && pinyin.StartsWith(query, StringComparison.Ordinal))
            {
                return 65;
            }

            // 子串：vscode 含 code / 双向都试；中文两字即可命中（软件名很少只有两字），其它仍要 3 字以免误判
            var minSubstringLength = ContainsChinese(query) ? 2 : 3;
            if (Math.Min(query.Length, candidateText.Length) >= minSubstringLength &&
                (candidateText.Contains(query, StringComparison.Ordinal) ||
                 query.Contains(candidateText, StringComparison.Ordinal)))
            {
                return 55;
            }

            // 跳字：vscode 是 visualstudiocode 的子序列
            if (query.Length >= 3 && IsSubsequence(query, candidateText))
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

        /// <summary>文本是否包含中日韩统一汉字。</summary>
        private static bool ContainsChinese(string text)
        {
            foreach (var c in text)
            {
                if (c >= '\u4E00' && c <= '\u9FFF')
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>收集全机候选池：App Paths、卸载登记项、开始菜单、桌面快捷方式与用户级安装目录。</summary>
        private static List<Candidate> CollectCandidates()
        {
            var candidates = new List<Candidate>();
            var counts = new List<string>();

            Append(candidates, counts, "App Paths", CollectAppPathCandidates);
            Append(candidates, counts, "卸载登记", CollectUninstallCandidates);
            Append(candidates, counts, "开始菜单", CollectStartMenuCandidates);
            Append(candidates, counts, "桌面", CollectDesktopCandidates);
            Append(candidates, counts, "用户级安装目录", CollectLocalProgramCandidates);

            var distinct = DeduplicateByPath(candidates);
            Log.Debug($"[打开应用] 候选池：{string.Join("、", counts)}；去重后 {distinct.Count} 条");
            return distinct;
        }

        /// <summary>执行一个来源的收集：单个来源异常只记录日志，不影响其它来源。</summary>
        private static void Append(List<Candidate> target, List<string> counts, string label,
            Func<List<Candidate>> collector)
        {
            try
            {
                var items = collector();
                counts.Add($"{label} {items.Count}");
                target.AddRange(items);
            }
            catch (Exception ex)
            {
                counts.Add($"{label} 失败");
                Log.Warning($"[打开应用] 收集{label}失败：{ex.Message}");
            }
        }

        /// <summary>按可执行路径去重，保留先出现的显示名。</summary>
        private static List<Candidate> DeduplicateByPath(List<Candidate> candidates)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Candidate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (seen.Add(candidate.Path))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        /// <summary>创建候选并预算拼音首字母。</summary>
        private static Candidate Create(string name, string path)
        {
            return new Candidate(name, path, PinyinInitials.From(name));
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
                                list.Add(Create(Path.GetFileNameWithoutExtension(subKey), path));
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

        /// <summary>卸载登记项：读取 DisplayName 与 DisplayIcon，只收指向 .exe 的条目，
        /// 覆盖没有开始菜单快捷方式的软件（商店应用除外）。</summary>
        private static List<Candidate> CollectUninstallCandidates()
        {
            var list = new List<Candidate>();
            var subPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var subPath in subPaths)
                {
                    try
                    {
                        using var root = hive.OpenSubKey(subPath);
                        if (root == null)
                        {
                            continue;
                        }

                        foreach (var subKeyName in root.GetSubKeyNames())
                        {
                            using var appKey = root.OpenSubKey(subKeyName);
                            if (appKey?.GetValue("DisplayName") is not string rawName ||
                                appKey.GetValue("DisplayIcon") is not string rawIcon)
                            {
                                continue;
                            }

                            var name = rawName.Trim();
                            var executable = ParseDisplayIcon(rawIcon);
                            if (name.Length == 0 || executable == null ||
                                !IsLikelyApplication(name) || !File.Exists(executable))
                            {
                                continue;
                            }

                            list.Add(Create(name, executable));
                        }
                    }
                    catch
                    {
                        // 单个注册表分支读取失败不影响其它来源
                    }
                }
            }

            return list;
        }

        /// <summary>从 DisplayIcon 取出可执行文件路径：去掉 “,0” 索引与引号，只接受 .exe。</summary>
        private static string? ParseDisplayIcon(string displayIcon)
        {
            var path = displayIcon.Trim();
            var comma = path.LastIndexOf(',');
            if (comma > 0)
            {
                path = path[..comma].Trim();
            }

            path = path.Trim('"').Trim();
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        /// <summary>过滤卸载登记里的运行库、补丁与语言包，避免它们进入候选名单。</summary>
        private static bool IsLikelyApplication(string displayName)
        {
            var lower = displayName.ToLowerInvariant();
            return !lower.Contains("redistributable") &&
                   !lower.Contains("update for") &&
                   !lower.Contains("security update") &&
                   !lower.Contains("hotfix") &&
                   !lower.Contains("language pack") &&
                   !lower.Contains("运行库") &&
                   !lower.Contains("补丁");
        }

        /// <summary>桌面（用户 + 公共）里的快捷方式。</summary>
        private static List<Candidate> CollectDesktopCandidates()
        {
            var list = new List<Candidate>();
            foreach (var folder in new[]
                     {
                         Environment.SpecialFolder.DesktopDirectory,
                         Environment.SpecialFolder.CommonDesktopDirectory
                     })
            {
                var directory = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                try
                {
                    foreach (var file in Directory.GetFiles(directory, "*.lnk"))
                    {
                        list.Add(Create(Path.GetFileNameWithoutExtension(file), file));
                    }
                }
                catch
                {
                    // 单个桌面目录枚举失败不影响其它来源
                }
            }

            return list;
        }

        /// <summary>用户级安装目录（%LocalAppData%\Programs）里的主程序：VS Code、Discord 等常装在这里，
        /// 且不一定生成开始菜单快捷方式。</summary>
        private static List<Candidate> CollectLocalProgramCandidates()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            if (!Directory.Exists(root))
            {
                return new List<Candidate>();
            }

            var list = new List<Candidate>();
            CollectExecutablesRecursive(root, list, depth: 0);
            return list;
        }

        /// <summary>递归收集可执行文件：限制深度与数量，并过滤卸载、更新、崩溃上报等噪音程序。</summary>
        private static void CollectExecutablesRecursive(string directory, List<Candidate> list, int depth)
        {
            if (depth > MaxLocalProgramDepth || list.Count >= MaxLocalProgramExecutables)
            {
                return;
            }

            try
            {
                foreach (var file in Directory.GetFiles(directory, "*.exe"))
                {
                    if (list.Count >= MaxLocalProgramExecutables)
                    {
                        return;
                    }

                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (IsLikelyMainExecutable(fileName))
                    {
                        list.Add(Create(fileName, file));
                    }
                }
            }
            catch
            {
                // 单个目录枚举失败不影响其它目录
            }

            try
            {
                foreach (var subDirectory in Directory.GetDirectories(directory))
                {
                    if (list.Count >= MaxLocalProgramExecutables)
                    {
                        return;
                    }

                    CollectExecutablesRecursive(subDirectory, list, depth + 1);
                }
            }
            catch
            {
                // 单个子目录枚举失败不影响其它目录
            }
        }

        /// <summary>排除更新器、卸载器、崩溃上报等辅助程序。</summary>
        private static bool IsLikelyMainExecutable(string fileName)
        {
            var lower = fileName.ToLowerInvariant();
            return !(lower.Contains("unins") || lower.Contains("setup") || lower.Contains("update") ||
                     lower.Contains("install") || lower.Contains("helper") || lower.Contains("crash") ||
                     lower.Contains("report") || lower.Contains("elevate") || lower.Contains("repair"));
        }

        /// <summary>收集开始菜单（用户 + 公共）里的 .lnk，名称取快捷方式文件名、路径直接给 .lnk。
        /// 逐目录递归并单目录容错：某子目录无权限或枚举失败时跳过，避免整轮扫描因一处异常全部落空。</summary>
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

                    list.Add(Create(Path.GetFileNameWithoutExtension(file), file));
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
