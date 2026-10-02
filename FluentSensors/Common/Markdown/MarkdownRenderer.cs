using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;


namespace FluentSensors.Common.Markdown
{
    // the markdown renderer:
    // renders the body of a GitHub release; not a general implementation, a release body is headings, lists, bold,
    // inline code, links and callouts (the one library is a 0.1.x preview)
    // anything unrecognised falls through as plain text instead of disappearing
    // renders into a Panel, since RichTextBlock.Blocks only takes Paragraph and a callout needs a Border for its rule
    public static class MarkdownRenderer
    {
        // === layout constants ===

        // --- heading sizes and spacing (below full page sizes, a release dialog is narrow) ---
        private const double H1FontSize = 20;
        private const double H2FontSize = 17;
        private const double H3FontSize = 15;
        private const double HeadingTopMargin = 16; // except the very first one
        private const double HeadingBottomMargin = 5;
        private const double ParagraphBottomMargin = 9;
        private const double ListItemBottomMargin = 3;

        // a list packs its items tight, so the paragraph after it brings the gap
        private const double ParagraphTopMarginAfterList = 18;

        // running text rhythm; a minimum (see NewTextBlock), so a heading keeps its taller line box
        private const double BodyLineHeight = 22;
        private const double BulletIndent = 16; // list item inset
        private const double BulletHang = -11; // pulls the marker back out of it
        private const double InlineImageMaxWidth = 420;

        // --- callout geometry ---
        private const double AlertRuleThickness = 3; // the bar down the left
        private const double AlertInset = 14; // bar to text


        // === callout colours ===

        // GitHub Primer, the values github.com uses; one light and dark pair per alert kind
        private static readonly Dictionary<string, (Color Light, Color Dark)> AlertColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NOTE"] = (Rgb(0x09, 0x69, 0xDA), Rgb(0x44, 0x93, 0xF8)),
            ["TIP"] = (Rgb(0x1A, 0x7F, 0x37), Rgb(0x3F, 0xB9, 0x50)),
            ["IMPORTANT"] = (Rgb(0x82, 0x50, 0xDF), Rgb(0xA3, 0x71, 0xF7)),
            ["WARNING"] = (Rgb(0x9A, 0x67, 0x00), Rgb(0xD2, 0x99, 0x22)),
            ["CAUTION"] = (Rgb(0xCF, 0x22, 0x2E), Rgb(0xF8, 0x51, 0x49)),
        };

        private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);


        // === dropped sections ===

        // a case insensitive substring of the heading, so an emoji in front does not matter
        private static readonly string[] DroppedSections = { "Installation" };


        // === patterns ===

        private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex BulletPattern = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex NumberedPattern = new(@"^\s*(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex RulePattern = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled);

        private static readonly Regex QuotePattern = new(@"^\s*>\s?(.*)$", RegexOptions.Compiled);

        // GitHub callouts inside a blockquote; otherwise "[!IMPORTANT]" would show as text
        private static readonly Regex AlertPattern = new(
            @"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // one pass over every inline form in precedence order, so "**bold**" is no italic pair and
        // an image leaves no stray "!"
        // no underscore emphasis, it would mangle identifiers like Some_Name_Here
        private static readonly Regex InlinePattern = new(
            @"(?<image>!\[(?<imageAlt>[^\]]*)\]\((?<imageUrl>[^\s)]+)\))" +
            @"|(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^\s)]+)\))" +
            @"|(?<bold>\*\*(?<boldText>.+?)\*\*)" +
            @"|(?<code>`(?<codeText>[^`]+)`)" +
            @"|(?<url>https?://[^\s<>""]+)" +
            @"|(?<italic>\*(?<italicText>[^*\s][^*]*?)\*)",
            RegexOptions.Compiled);

        // markdown and the html GitHub writes for a pasted image; the first one is the header image
        private static readonly Regex StandaloneImagePattern = new(
            @"!\[[^\]]*\]\((?<imageUrl>[^\s)]+)\)" +
            @"|<img[^>]*?\ssrc\s*=\s*[""'](?<imageUrl>[^""']+)[""'][^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HtmlImagePattern = new(@"<img[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a bare row of hashes, which GitHub leaves behind
        private static readonly Regex EmptyHeadingPattern = new(@"^\s*#{1,6}\s*$", RegexOptions.Compiled);

        // a release body ends on this line; it survives every section removal
        private static readonly Regex ChangelogLinkPattern = new(@"^\s*\*\*Full Changelog\*\*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // all ExtractChangelogLink keeps of that line
        private static readonly Regex UrlPattern = new(@"https?://[^\s<>""]+", RegexOptions.Compiled);


        // === public api ===

        // lifts the first image out as the header image, so it does not show in the running text too
        public static string ExtractLeadingImage(string markdown, out string imageUrl)
        {
            imageUrl = null;
            if (string.IsNullOrWhiteSpace(markdown)) return markdown;

            var match = StandaloneImagePattern.Match(markdown);
            if (!match.Success) return markdown;

            imageUrl = match.Groups["imageUrl"].Value;
            string rest = markdown.Remove(match.Index, match.Length);

            // leftover html images would show as raw tags
            return HtmlImagePattern.Replace(rest, "");
        }

        // strips sections the dialog has no use for (installation steps), down to the next
        // heading or the full changelog line
        public static string DropSections(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return markdown;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var kept = new List<string>();
            bool dropping = false;

            foreach (string line in lines)
            {
                var heading = HeadingPattern.Match(line);

                if (heading.Success)
                {
                    string title = heading.Groups[2].Value;
                    dropping = DroppedSections.Any(name => title.Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (dropping) continue;
                }
                else if (dropping && ChangelogLinkPattern.IsMatch(line))
                {
                    dropping = false;
                }

                if (!dropping) kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        // lifts the full changelog line out and returns its url; the release page shows it as its own XAML row, with a
        // themed label and a link button
        public static string ExtractChangelogLink(string markdown, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(markdown)) return markdown;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var kept = new List<string>();

            foreach (string line in lines)
            {
                if (url == null && ChangelogLinkPattern.IsMatch(line))
                {
                    string found = UrlPattern.Match(line).Value;

                    // without a usable address the line stays in the body
                    if (found.Length > 0)
                    {
                        url = found;
                        continue;
                    }
                }

                kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        // replaces the target content, so re-rendering is safe
        public static void Render(Panel target, string markdown)
        {
            if (target == null) return;

            target.Children.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;

            // ActualTheme, the app sets its theme per element
            bool isDark = target.ActualTheme == ElementTheme.Dark;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var writer = new BlockWriter(target);

            // consecutive plain lines, so a soft-wrapped paragraph stays one
            var pending = new List<string>();

            void FlushPending()
            {
                if (pending.Count == 0) return;

                double topMargin = writer.LastBlockWasListItem ? ParagraphTopMarginAfterList : 0;

                var paragraph = new Paragraph { Margin = new Thickness(0, topMargin, 0, ParagraphBottomMargin) };
                AppendInlines(paragraph, string.Join(" ", pending));
                writer.Add(paragraph);

                pending.Clear();
            }

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();

                // a blank line, a rule or an empty heading breaks and closes an open callout
                if (string.IsNullOrWhiteSpace(line) || RulePattern.IsMatch(line) || EmptyHeadingPattern.IsMatch(line))
                {
                    FlushPending();
                    writer.EndAlert();
                    continue;
                }

                var quote = QuotePattern.Match(line);
                if (quote.Success)
                {
                    FlushPending();

                    string inner = quote.Groups[1].Value.Trim();
                    var alert = AlertPattern.Match(inner);

                    if (alert.Success) writer.BeginAlert(alert.Groups[1].Value, isDark);
                    else if (inner.Length > 0) writer.Add(BuildQuote(inner, writer.IsInAlert));

                    continue;
                }

                // any non-quote line ends the callout
                writer.EndAlert();

                var heading = HeadingPattern.Match(line);
                if (heading.Success)
                {
                    FlushPending();
                    writer.Add(BuildHeading(heading.Groups[1].Value.Length, heading.Groups[2].Value, writer.IsFirstBlock));
                    continue;
                }

                var numbered = NumberedPattern.Match(line);
                if (numbered.Success)
                {
                    FlushPending();
                    writer.Add(BuildListItem($"{numbered.Groups[1].Value}.", numbered.Groups[2].Value), isListItem: true);
                    continue;
                }

                var bullet = BulletPattern.Match(line);
                if (bullet.Success)
                {
                    FlushPending();
                    writer.Add(BuildListItem("•", bullet.Groups[1].Value), isListItem: true);
                    continue;
                }

                pending.Add(line.Trim());
            }

            FlushPending();
            writer.EndAlert();
        }


        // === block writer ===

        // the running RichTextBlock, swapped for the callout one while an alert is open, inside a Border for the rule
        private sealed class BlockWriter
        {
            private readonly Panel _target;
            private RichTextBlock _current;
            private bool _inAlert;
            private bool _anyBlockWritten;
            private bool _lastWasListItem;

            public BlockWriter(Panel target) => _target = target;

            public bool IsInAlert => _inAlert;

            // until the first block lands; keeps a leading heading flush with the top
            public bool IsFirstBlock => !_anyBlockWritten;

            // so the paragraph after a list brings its gap
            public bool LastBlockWasListItem => _lastWasListItem;

            public void Add(Block block, bool isListItem = false)
            {
                _current ??= StartTextBlock();
                _current.Blocks.Add(block);
                _anyBlockWritten = true;
                _lastWasListItem = isListItem;
            }

            public void BeginAlert(string kind, bool isDark)
            {
                if (_inAlert) return;

                _inAlert = true;

                Color color = AlertColors.TryGetValue(kind, out var pair)
                    ? (isDark ? pair.Dark : pair.Light)
                    : AlertColors["IMPORTANT"].Light;

                var brush = new SolidColorBrush(color);
                var body = NewTextBlock();

                var label = new TextBlock
                {
                    Text = Title(kind),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = brush,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var stack = new StackPanel();
                stack.Children.Add(label);
                stack.Children.Add(body);

                // the rule is the Border left edge, so it spans the whole callout
                _target.Children.Add(new Border
                {
                    BorderBrush = brush,
                    BorderThickness = new Thickness(AlertRuleThickness, 0, 0, 0),
                    Padding = new Thickness(AlertInset, 2, 0, 2),
                    Margin = new Thickness(0, 8, 0, 8),
                    Child = stack
                });

                // everything until EndAlert lands in the callout
                _current = body;
                _anyBlockWritten = true;
                _lastWasListItem = false;
            }

            public void EndAlert()
            {
                if (!_inAlert) return;

                _inAlert = false;
                _current = null;
            }

            private RichTextBlock StartTextBlock()
            {
                var block = NewTextBlock();
                _target.Children.Add(block);

                return block;
            }

            // MaxHeight, so the line height is a floor; text loosens up and a heading still gets its room
            private static RichTextBlock NewTextBlock() => new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = BodyLineHeight,
                LineStackingStrategy = LineStackingStrategy.MaxHeight
            };

            private static string Title(string kind) =>
                kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind.Substring(1).ToLowerInvariant();
        }


        // === block builders ===

        private static Paragraph BuildHeading(int level, string text, bool isFirstBlock)
        {
            double fontSize = level switch
            {
                1 => H1FontSize,
                2 => H2FontSize,
                _ => H3FontSize
            };

            // a leading heading sits flush with the top
            var paragraph = new Paragraph
            {
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, isFirstBlock ? 0 : HeadingTopMargin, 0, HeadingBottomMargin)
            };

            AppendInlines(paragraph, text);
            return paragraph;
        }

        // a quote inside a callout sits behind the rule already, without inset and dimming
        private static Paragraph BuildQuote(string text, bool isInAlert)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(isInAlert ? 0 : BulletIndent, 0, 0, ListItemBottomMargin)
            };

            if (!isInAlert)
            {
                var brush = ThemeBrush("TextFillColorSecondaryBrush");
                if (brush != null) paragraph.Foreground = brush;
            }

            AppendInlines(paragraph, text);
            return paragraph;
        }

        // resolves against the application theme, fixed at process start and blind to a theme switch; only blockquotes
        // use it, which is why the changelog row is XAML
        private static Brush ThemeBrush(string key) =>
            Application.Current.Resources.TryGetValue(key, out object value) ? value as Brush : null;

        // hanging indent, so wrapped lines align under the text, not the bullet
        private static Paragraph BuildListItem(string marker, string text)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(BulletIndent, 0, 0, ListItemBottomMargin),
                TextIndent = BulletHang
            };

            paragraph.Inlines.Add(new Run { Text = $"{marker}  " });
            AppendInlines(paragraph, text);

            return paragraph;
        }


        // === inline parsing ===

        private static void AppendInlines(Paragraph paragraph, string text)
        {
            int position = 0;

            foreach (Match match in InlinePattern.Matches(text))
            {
                if (match.Index > position)
                {
                    paragraph.Inlines.Add(new Run { Text = text.Substring(position, match.Index - position) });
                }

                paragraph.Inlines.Add(BuildInline(match));
                position = match.Index + match.Length;
            }

            if (position < text.Length)
            {
                paragraph.Inlines.Add(new Run { Text = text.Substring(position) });
            }
        }

        private static Inline BuildInline(Match match)
        {
            if (match.Groups["image"].Success)
            {
                return BuildImage(match.Groups["imageUrl"].Value);
            }

            if (match.Groups["link"].Success)
            {
                return BuildHyperlink(match.Groups["linkText"].Value, match.Groups["linkUrl"].Value);
            }

            if (match.Groups["url"].Success)
            {
                // a url ending a sentence would swallow the punctuation
                string url = match.Groups["url"].Value.TrimEnd('.', ',', ';', ':', ')');
                return BuildHyperlink(url, url);
            }

            if (match.Groups["bold"].Success)
            {
                return new Run { Text = match.Groups["boldText"].Value, FontWeight = FontWeights.SemiBold };
            }

            if (match.Groups["code"].Success)
            {
                return new Run
                {
                    Text = match.Groups["codeText"].Value,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New")
                };
            }

            return new Run { Text = match.Groups["italicText"].Value, FontStyle = FontStyle.Italic };
        }

        // an extra image in the running text; loads from its url, so it stays blank offline (unlike the header image,
        // which ReleaseCatalog keeps on disk)
        private static Inline BuildImage(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new Run { Text = "" };
            }

            var image = new Image
            {
                Source = new BitmapImage(uri),
                Stretch = Stretch.Uniform,
                MaxWidth = InlineImageMaxWidth,
                Margin = new Thickness(0, 4, 0, 4)
            };

            return new InlineUIContainer { Child = image };
        }

        // a malformed url falls back to its plain text instead of costing the whole notes
        private static Inline BuildHyperlink(string text, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new Run { Text = text };
            }

            var hyperlink = new Hyperlink { NavigateUri = uri };
            hyperlink.Inlines.Add(new Run { Text = text });

            return hyperlink;
        }
    }
}
