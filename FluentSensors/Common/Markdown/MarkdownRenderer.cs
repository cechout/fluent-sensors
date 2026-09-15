using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.UI.Text;


namespace FluentSensors.Common.Markdown
{
    // turns the markdown body of a GitHub release into RichTextBlock content
    //
    // deliberately not a general markdown implementation and not a library: the only input it ever sees is a
    // release body, which in practice is headings, bullet lists, bold runs, inline code and links, and the one
    // package that would cover the rest is a 0.1.x preview that would ship in a release build
    // anything it does not recognise falls through as plain text rather than being dropped, so an unexpected
    // construct degrades to something readable instead of disappearing
    public static class MarkdownRenderer
    {
        // === layout constants ===

        // --- heading sizes and spacing (a release notes flyout is narrow, so these sit well below the sizes a
        // full page would use) ---
        private const double H1FontSize = 20;
        private const double H2FontSize = 17;
        private const double H3FontSize = 15;
        private const double HeadingTopMargin = 16; // gap above a heading, except the very first one
        private const double HeadingBottomMargin = 5;
        private const double ParagraphBottomMargin = 9;
        private const double ListItemBottomMargin = 3;
        private const double BulletIndent = 16; // left inset of a list item
        private const double BulletHang = -11; // pulls the marker itself back out of that inset


        // === patterns ===

        private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex BulletPattern = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex NumberedPattern = new(@"^\s*(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex RulePattern = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled);

        private static readonly Regex QuotePattern = new(@"^\s*>\s?(.*)$", RegexOptions.Compiled);

        // GitHub renders these as a coloured callout inside a blockquote; without their own case the marker line
        // would show up as literal "[!IMPORTANT]" text in the middle of the notes
        private static readonly Regex AlertPattern = new(
            @"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // one pass over every inline form that is supported, in precedence order; the alternation is what keeps
        // "**bold**" from being read as an italic star pair
        // underscore emphasis is deliberately absent: release notes carry identifiers like Some_Name_Here far more
        // often than they carry underscore italics, and treating those as markup mangles them
        private static readonly Regex InlinePattern = new(
            @"(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^\s)]+)\))" +
            @"|(?<bold>\*\*(?<boldText>.+?)\*\*)" +
            @"|(?<code>`(?<codeText>[^`]+)`)" +
            @"|(?<url>https?://[^\s<>""]+)" +
            @"|(?<italic>\*(?<italicText>[^*\s][^*]*?)\*)",
            RegexOptions.Compiled);


        // === public api ===

        // replaces whatever the target currently holds, so re-rendering the same block is safe
        public static void Render(RichTextBlock target, string markdown)
        {
            if (target == null) return;

            target.Blocks.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // collects consecutive plain lines so a soft-wrapped paragraph stays one paragraph
            var pending = new List<string>();
            bool isFirstBlock = true;

            void FlushPending()
            {
                if (pending.Count == 0) return;

                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, ParagraphBottomMargin) };
                AppendInlines(paragraph, string.Join(" ", pending));
                target.Blocks.Add(paragraph);

                pending.Clear();
                isFirstBlock = false;
            }

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPending();
                    continue;
                }

                // a horizontal rule has no equivalent inside RichTextBlock, whose Blocks only take paragraphs;
                // it reads as a separator, so it becomes the paragraph break it already implies
                if (RulePattern.IsMatch(line))
                {
                    FlushPending();
                    continue;
                }

                var heading = HeadingPattern.Match(line);
                if (heading.Success)
                {
                    FlushPending();
                    target.Blocks.Add(BuildHeading(heading.Groups[1].Value.Length, heading.Groups[2].Value, isFirstBlock));
                    isFirstBlock = false;
                    continue;
                }

                var quote = QuotePattern.Match(line);
                if (quote.Success)
                {
                    FlushPending();

                    string inner = quote.Groups[1].Value.Trim();
                    var alert = AlertPattern.Match(inner);

                    if (alert.Success) target.Blocks.Add(BuildAlertLabel(alert.Groups[1].Value));
                    else if (inner.Length > 0) target.Blocks.Add(BuildQuote(inner));

                    isFirstBlock = false;
                    continue;
                }

                var numbered = NumberedPattern.Match(line);
                if (numbered.Success)
                {
                    FlushPending();
                    target.Blocks.Add(BuildListItem($"{numbered.Groups[1].Value}.", numbered.Groups[2].Value));
                    isFirstBlock = false;
                    continue;
                }

                var bullet = BulletPattern.Match(line);
                if (bullet.Success)
                {
                    FlushPending();
                    target.Blocks.Add(BuildListItem("•", bullet.Groups[1].Value));
                    isFirstBlock = false;
                    continue;
                }

                pending.Add(line.Trim());
            }

            FlushPending();
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

            // the leading heading sits flush with the top of the flyout, everything after it gets its gap
            var paragraph = new Paragraph
            {
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, isFirstBlock ? 0 : HeadingTopMargin, 0, HeadingBottomMargin)
            };

            AppendInlines(paragraph, text);
            return paragraph;
        }

        // the callout marker line, rendered as the word it stands for rather than the raw tag
        private static Paragraph BuildAlertLabel(string kind)
        {
            var paragraph = new Paragraph
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(BulletIndent, HeadingTopMargin / 2, 0, 2)
            };

            var brush = ThemeBrush("AccentTextFillColorPrimaryBrush");
            if (brush != null) paragraph.Foreground = brush;

            string text = kind.Length == 0
                ? kind
                : char.ToUpperInvariant(kind[0]) + kind.Substring(1).ToLowerInvariant();

            paragraph.Inlines.Add(new Run { Text = text });
            return paragraph;
        }

        private static Paragraph BuildQuote(string text)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(BulletIndent, 0, 0, ListItemBottomMargin)
            };

            var brush = ThemeBrush("TextFillColorSecondaryBrush");
            if (brush != null) paragraph.Foreground = brush;

            AppendInlines(paragraph, text);
            return paragraph;
        }

        // resolved once per render rather than bound, which is fine because the flyout rebuilds its content every
        // time it opens, so a theme switch is picked up on the next open
        private static Brush? ThemeBrush(string key) =>
            Application.Current.Resources.TryGetValue(key, out object value) ? value as Brush : null;

        // hanging indent: the inset moves the whole item right and the negative first-line indent pulls the
        // marker back out of it, so wrapped lines align under the text rather than under the bullet
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
            if (match.Groups["link"].Success)
            {
                return BuildHyperlink(match.Groups["linkText"].Value, match.Groups["linkUrl"].Value);
            }

            if (match.Groups["url"].Success)
            {
                // a url that ends a sentence swallows the punctuation without this, since none of it is illegal
                // in a url and the pattern cannot tell the two apart
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

        // a malformed url would throw on the Uri, and one bad link should not cost the whole release notes, so it
        // falls back to the plain text it was written as
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
