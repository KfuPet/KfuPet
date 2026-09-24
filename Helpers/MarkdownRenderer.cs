using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace KfuPet.Helpers
{
    /// <summary>
    /// 轻量 Markdown 渲染器：把更新日志一类的 Markdown 文本转换成 WPF 控件树。
    /// 支持标题、无序/有序列表、引用、代码块、分隔线，以及加粗、斜体、删除线、
    /// 行内代码、链接（图片按链接处理）等常用行内语法。
    /// 字体与颜色全部引用应用主题资源，随主题切换自动更新。
    /// </summary>
    public static class MarkdownRenderer
    {
        // 正文字号、字体与颜色资源键
        private const string BodyFontSizeKey = "FontSizeCaption";
        private const string BodyFontFamilyKey = "FontFamilyBody";
        private const string Level1FontSizeKey = "FontSizeSubtitle";
        private const string Level2FontSizeKey = "FontSizeBody";
        private const string PrimaryTextBrushKey = "AppTextPrimaryBrush";
        private const string SecondaryTextBrushKey = "AppTextSecondaryBrush";
        private const string AccentBrushKey = "AppAccentBrush";
        private const string BorderBrushKey = "AppBorderBrush";

        /// <summary>代码块等宽字体（带常见回退）。</summary>
        private const string CodeFontFamilyName = "Consolas, Cascadia Mono, Courier New";

        // 排版尺寸
        private const double BlockSpacing = 6;
        private const double ListIndentStep = 14;
        private const double ListMarkerSpacing = 6;
        private const double SeparatorThickness = 1;
        private const double SeparatorSpacing = 8;
        private const double QuoteBarWidth = 2;
        private const double QuotePadding = 10;
        private const double CodeBlockPadding = 10;
        private const double CodeBlockVerticalPadding = 8;
        private const double CodeBlockCornerRadius = 6;

        /// <summary>代码块底纹透明度：用次要文字色叠加出随主题变化的分区底色。</summary>
        private const double CodeBlockBackgroundOpacity = 0.08;

        // 解析规则
        private const int MaxHeadingLevel = 6;
        private const int HorizontalRuleMinLength = 3;
        private const int ListNestingSpaces = 2;
        private const int MaxListIndentLevel = 3;
        private const int TabWidth = 4;

        /// <summary>
        /// 把 Markdown 文本渲染到指定面板中，渲染前会先清空面板已有内容。
        /// </summary>
        /// <param name="markdown">Markdown 文本，允许为 null 或空白（此时只做清空）。</param>
        /// <param name="container">承载渲染结果的面板。</param>
        public static void Render(string? markdown, Panel container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            container.Children.Clear();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return;
            }

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var index = 0;
            while (index < lines.Length)
            {
                var rawLine = lines[index];
                var line = rawLine.Trim();

                if (line.Length == 0)
                {
                    index++;
                    continue;
                }

                // 代码块：``` 开始，直到配对围栏或文本结束
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    index++;
                    var code = new StringBuilder();
                    while (index < lines.Length &&
                           !lines[index].Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        if (code.Length > 0)
                        {
                            code.Append('\n');
                        }

                        code.Append(lines[index]);
                        index++;
                    }

                    if (index < lines.Length)
                    {
                        index++; // 跳过结束围栏
                    }

                    container.Children.Add(CreateCodeBlock(code.ToString()));
                    continue;
                }

                // 分隔线
                if (IsHorizontalRule(line))
                {
                    container.Children.Add(CreateSeparator());
                    index++;
                    continue;
                }

                // 标题
                var headingLevel = GetHeadingLevel(line);
                if (headingLevel > 0)
                {
                    var headingText = TrimHeadingText(line.Substring(headingLevel));
                    container.Children.Add(CreateHeading(headingLevel, ParseInlines(headingText)));
                    index++;
                    continue;
                }

                // 引用：连续的 > 行合并成一段
                if (line.StartsWith(">", StringComparison.Ordinal))
                {
                    var quote = new StringBuilder();
                    while (index < lines.Length &&
                           lines[index].Trim().StartsWith(">", StringComparison.Ordinal))
                    {
                        var quoteLine = lines[index].Trim().Substring(1).Trim();
                        if (quote.Length > 0)
                        {
                            quote.Append(' ');
                        }

                        quote.Append(quoteLine);
                        index++;
                    }

                    container.Children.Add(CreateQuote(ParseInlines(quote.ToString())));
                    continue;
                }

                // 列表项：每种标记单独成行
                if (TryGetListMarker(line, out var marker))
                {
                    var itemText = line.Substring(marker.Length).Trim();
                    container.Children.Add(CreateListItem(marker, GetIndentLevel(rawLine), ParseInlines(itemText)));
                    index++;
                    continue;
                }

                // 普通段落：连续的普通行合并成一段
                var paragraph = new StringBuilder();
                while (index < lines.Length)
                {
                    var paragraphLine = lines[index].Trim();
                    if (paragraphLine.Length == 0 || IsBlockStart(paragraphLine))
                    {
                        break;
                    }

                    if (paragraph.Length > 0)
                    {
                        paragraph.Append(' ');
                    }

                    paragraph.Append(paragraphLine);
                    index++;
                }

                container.Children.Add(CreateParagraph(ParseInlines(paragraph.ToString())));
            }
        }

        // ---------- 块级元素 ----------

        /// <summary>段落：次要色正文，自动换行。</summary>
        private static TextBlock CreateParagraph(IReadOnlyList<Inline> inlines)
        {
            var textBlock = CreateBodyTextBlock();
            textBlock.Margin = new Thickness(0, 0, 0, BlockSpacing);
            foreach (var inline in inlines)
            {
                textBlock.Inlines.Add(inline);
            }

            return textBlock;
        }

        /// <summary>标题：主色加粗，字号按层级递减。</summary>
        private static TextBlock CreateHeading(int level, IReadOnlyList<Inline> inlines)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, BlockSpacing)
            };
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, BodyFontFamilyKey);
            textBlock.SetResourceReference(TextBlock.FontSizeProperty, level switch
            {
                1 => Level1FontSizeKey,
                2 => Level2FontSizeKey,
                _ => BodyFontSizeKey
            });
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, PrimaryTextBrushKey);

            foreach (var inline in inlines)
            {
                textBlock.Inlines.Add(inline);
            }

            return textBlock;
        }

        /// <summary>列表项：项目符号与正文分列，多行文本悬挂对齐，按缩进层级左侧留白。</summary>
        private static FrameworkElement CreateListItem(string marker, int indentLevel, IReadOnlyList<Inline> inlines)
        {
            var bullet = new TextBlock
            {
                Text = char.IsDigit(marker[0]) ? marker.TrimEnd() : "•",
                Margin = new Thickness(0, 0, ListMarkerSpacing, 0)
            };
            ApplyBodyStyle(bullet);

            var content = CreateBodyTextBlock();
            foreach (var inline in inlines)
            {
                content.Inlines.Add(inline);
            }

            var grid = new Grid { Margin = new Thickness(indentLevel * ListIndentStep, 0, 0, BlockSpacing) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(bullet, 0);
            Grid.SetColumn(content, 1);
            grid.Children.Add(bullet);
            grid.Children.Add(content);
            return grid;
        }

        /// <summary>引用块：左侧竖条 + 缩进正文。</summary>
        private static Border CreateQuote(IReadOnlyList<Inline> inlines)
        {
            var content = CreateBodyTextBlock();
            foreach (var inline in inlines)
            {
                content.Inlines.Add(inline);
            }

            var quote = new Border
            {
                BorderThickness = new Thickness(QuoteBarWidth, 0, 0, 0),
                Padding = new Thickness(QuotePadding, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, BlockSpacing),
                Child = content
            };
            quote.SetResourceReference(Border.BorderBrushProperty, BorderBrushKey);
            return quote;
        }

        /// <summary>代码块：半透明底纹（随主题变化）+ 等宽字体，底纹与文字分层避免文字被一起透明。</summary>
        private static Grid CreateCodeBlock(string code)
        {
            var text = new TextBlock
            {
                Text = code,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily(CodeFontFamilyName)
            };
            text.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeKey);
            text.SetResourceReference(TextBlock.ForegroundProperty, PrimaryTextBrushKey);

            var background = new Border
            {
                CornerRadius = new CornerRadius(CodeBlockCornerRadius),
                Opacity = CodeBlockBackgroundOpacity
            };
            background.SetResourceReference(Border.BackgroundProperty, SecondaryTextBrushKey);

            var content = new Border
            {
                Padding = new Thickness(CodeBlockPadding, CodeBlockVerticalPadding, CodeBlockPadding, CodeBlockVerticalPadding),
                Child = text
            };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, BlockSpacing) };
            grid.Children.Add(background);
            grid.Children.Add(content);
            return grid;
        }

        /// <summary>分隔线：贯穿可用宽度的一根细线。</summary>
        private static Border CreateSeparator()
        {
            var separator = new Border
            {
                Height = SeparatorThickness,
                Margin = new Thickness(0, SeparatorSpacing, 0, SeparatorSpacing)
            };
            separator.SetResourceReference(Border.BackgroundProperty, BorderBrushKey);
            return separator;
        }

        // ---------- 行内元素 ----------

        /// <summary>
        /// 解析行内语法。
        /// 支持 <c>`代码`</c>、<c>**加粗**</c>、<c>*斜体*</c>、<c>~~删除线~~</c>、<c>[文字](链接)</c>。
        /// </summary>
        private static IReadOnlyList<Inline> ParseInlines(string text)
        {
            var inlines = new List<Inline>();
            var plain = new StringBuilder();
            var index = 0;
            while (index < text.Length)
            {
                if (TryParseLink(text, index, out var linkEnd, out var label, out var url))
                {
                    FlushPlainText(inlines, plain);
                    inlines.Add(CreateHyperlink(label, url));
                    index = linkEnd;
                    continue;
                }

                if (text[index] == '`' &&
                    TryReadWrapped(text, index, "`", out var code, out var codeEnd))
                {
                    FlushPlainText(inlines, plain);
                    inlines.Add(CreateInlineCode(code));
                    index = codeEnd;
                    continue;
                }

                // 先匹配 ** 再匹配 *，避免把加粗拆成两个斜体
                if (TryReadWrapped(text, index, "**", out var boldText, out var boldEnd))
                {
                    FlushPlainText(inlines, plain);
                    inlines.Add(CreateSpan<Bold>(boldText));
                    index = boldEnd;
                    continue;
                }

                if (TryReadWrapped(text, index, "~~", out var strikeText, out var strikeEnd))
                {
                    FlushPlainText(inlines, plain);
                    var strike = CreateSpan<Span>(strikeText);
                    strike.TextDecorations = CreateStrikethrough();
                    inlines.Add(strike);
                    index = strikeEnd;
                    continue;
                }

                if (TryReadWrapped(text, index, "*", out var italicText, out var italicEnd))
                {
                    FlushPlainText(inlines, plain);
                    inlines.Add(CreateSpan<Italic>(italicText));
                    index = italicEnd;
                    continue;
                }

                plain.Append(text[index]);
                index++;
            }

            FlushPlainText(inlines, plain);
            return inlines;
        }

        /// <summary>带包裹标记的强调类元素，内部递归解析行内语法。</summary>
        private static T CreateSpan<T>(string content) where T : Span, new()
        {
            var span = new T();
            foreach (var inline in ParseInlines(content))
            {
                span.Inlines.Add(inline);
            }

            return span;
        }

        /// <summary>行内代码：等宽字体，主色。</summary>
        private static Run CreateInlineCode(string code)
        {
            var run = new Run(code) { FontFamily = new FontFamily(CodeFontFamilyName) };
            run.SetResourceReference(TextElement.ForegroundProperty, PrimaryTextBrushKey);
            return run;
        }

        /// <summary>删除线装饰：每次用独立且已冻结的副本，避免多个 Inline 共享同一 Freezable 实例。</summary>
        private static TextDecorationCollection CreateStrikethrough()
        {
            var decorations = TextDecorations.Strikethrough.Clone();
            decorations.Freeze();
            return decorations;
        }

        /// <summary>链接：强调色，点击调用系统默认浏览器；图片（![]()）按链接文本处理。</summary>
        private static Inline CreateHyperlink(string label, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var plain = new Run(label);
                plain.SetResourceReference(TextElement.ForegroundProperty, SecondaryTextBrushKey);
                return plain;
            }

            var link = new Hyperlink(new Run(label))
            {
                NavigateUri = uri,
                ToolTip = url
            };
            link.SetResourceReference(TextElement.ForegroundProperty, AccentBrushKey);
            link.RequestNavigate += OnRequestNavigate;
            return link;
        }

        private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // 打不开浏览器时忽略，不影响界面
            }
        }

        // ---------- 解析辅助 ----------

        /// <summary>判断该行是否是块级语法的起始行（用于合并段落时断句）。</summary>
        private static bool IsBlockStart(string line)
        {
            return line.StartsWith("```", StringComparison.Ordinal) ||
                   IsHorizontalRule(line) ||
                   GetHeadingLevel(line) > 0 ||
                   line.StartsWith(">", StringComparison.Ordinal) ||
                   TryGetListMarker(line, out _);
        }

        /// <summary>返回行首的 # 个数（1-6）；不是标题返回 0。中英文都兼容（# 后可不带空格）。</summary>
        private static int GetHeadingLevel(string line)
        {
            var level = 0;
            while (level < line.Length && level < MaxHeadingLevel && line[level] == '#')
            {
                level++;
            }

            if (level == 0 || level >= line.Length || line[level] == '#')
            {
                return 0;
            }

            return level;
        }

        /// <summary>去掉标题收尾的空格与用于闭合的 #（如 “## 标题 ##”）。</summary>
        private static string TrimHeadingText(string text)
        {
            return text.Trim().TrimEnd('#').TrimEnd();
        }

        /// <summary>三个及以上的 -、* 或 _（可含空格）视为分隔线。</summary>
        private static bool IsHorizontalRule(string line)
        {
            var count = 0;
            var character = '\0';
            foreach (var current in line)
            {
                if (current == ' ')
                {
                    continue;
                }

                if (current != '-' && current != '*' && current != '_')
                {
                    return false;
                }

                if (character == '\0')
                {
                    character = current;
                }
                else if (current != character)
                {
                    return false;
                }

                count++;
            }

            return count >= HorizontalRuleMinLength;
        }

        /// <summary>识别无序（- * +）与有序（1. / 1)）列表标记，返回的标记包含其后的空格。</summary>
        private static bool TryGetListMarker(string line, out string marker)
        {
            marker = string.Empty;

            if (line.Length >= 2 &&
                (line[0] == '-' || line[0] == '*' || line[0] == '+') &&
                line[1] == ' ')
            {
                marker = line.Substring(0, 2);
                return true;
            }

            var digits = 0;
            while (digits < line.Length && char.IsDigit(line[digits]))
            {
                digits++;
            }

            if (digits > 0 &&
                digits + 1 < line.Length &&
                (line[digits] == '.' || line[digits] == ')') &&
                line[digits + 1] == ' ')
            {
                marker = line.Substring(0, digits + 2);
                return true;
            }

            return false;
        }

        /// <summary>按行首空白换算列表缩进层级（两格一级，最多三级）。</summary>
        private static int GetIndentLevel(string rawLine)
        {
            var spaces = 0;
            foreach (var current in rawLine)
            {
                if (current == ' ')
                {
                    spaces++;
                }
                else if (current == '\t')
                {
                    spaces += TabWidth;
                }
                else
                {
                    break;
                }
            }

            var level = spaces / ListNestingSpaces;
            return Math.Min(level, MaxListIndentLevel);
        }

        /// <summary>
        /// 尝试从 <paramref name="start"/> 起读取 “标记 … 标记” 形式的行内片段。
        /// </summary>
        private static bool TryReadWrapped(string text, int start, string delimiter, out string content, out int next)
        {
            content = string.Empty;
            next = start;

            if (start + delimiter.Length > text.Length ||
                string.CompareOrdinal(text, start, delimiter, 0, delimiter.Length) != 0)
            {
                return false;
            }

            var close = text.IndexOf(delimiter, start + delimiter.Length, StringComparison.Ordinal);
            if (close <= start + delimiter.Length)
            {
                return false; // 未闭合或内容为空
            }

            content = text.Substring(start + delimiter.Length, close - start - delimiter.Length);
            next = close + delimiter.Length;
            return true;
        }

        /// <summary>尝试解析 [文字](链接) 或 ![替代文字](链接)。</summary>
        private static bool TryParseLink(string text, int start, out int next, out string label, out string url)
        {
            next = start;
            label = string.Empty;
            url = string.Empty;

            var index = start;
            var isImage = text[index] == '!';
            if (isImage)
            {
                index++;
            }

            if (index >= text.Length || text[index] != '[')
            {
                return false;
            }

            var labelEnd = text.IndexOf(']', index + 1);
            if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            {
                return false;
            }

            var urlEnd = text.IndexOf(')', labelEnd + 2);
            if (urlEnd < 0)
            {
                return false;
            }

            label = text.Substring(index + 1, labelEnd - index - 1);
            url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2);
            if (url.Length == 0)
            {
                return false;
            }

            if (label.Length == 0)
            {
                label = isImage ? "图片" : url;
            }

            next = urlEnd + 1;
            return true;
        }

        // ---------- 样式辅助 ----------

        /// <summary>创建正文样式（次要色、正文字号、自动换行）的文本块。</summary>
        private static TextBlock CreateBodyTextBlock()
        {
            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            ApplyBodyStyle(textBlock);
            return textBlock;
        }

        private static void ApplyBodyStyle(TextBlock textBlock)
        {
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, BodyFontFamilyKey);
            textBlock.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeKey);
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, SecondaryTextBrushKey);
        }

        private static void FlushPlainText(List<Inline> inlines, StringBuilder plain)
        {
            if (plain.Length == 0)
            {
                return;
            }

            inlines.Add(new Run(plain.ToString()));
            plain.Clear();
        }
    }
}