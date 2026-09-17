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
    // turns the markdown body of a GitHub release into rendered content
    //
    // deliberately not a general markdown implementation and not a library: the only input it ever sees is a
    // release body, which in practice is headings, bullet lists, bold runs, inline code, links and GitHub
    // callouts, and the one package that would cover the rest is a 0.1.x preview that would ship in a release
    // build
    // anything it does not recognise falls through as plain text rather than being dropped, so an unexpected
    // construct degrades to something readable instead of disappearing
    //
    // renders into a Panel rather than a single RichTextBlock, because a RichTextBlocks Blocks only take
    // Paragraph, and a callout needs a real Border to draw its rule down the side of several paragraphs
    public static class MarkdownRenderer
    {
        // === layout constants ===

        // --- heading sizes and spacing (a release dialog is narrow, so these sit well below the sizes a full
        // page would use) ---
        private const double H1FontSize = 20;
        private const double H2FontSize = 17;
        private const double H3FontSize = 15;
        private const double HeadingTopMargin = 16; // gap above a heading, except the very first one
        private const double HeadingBottomMargin = 5;
        private const double ParagraphBottomMargin = 9;
        private const double ListItemBottomMargin = 3;

        // a list packs its items tight, so a paragraph that follows one has to bring the gap itself; without it
        // the full changelog line ends up sitting on the last bullet
        private const double ParagraphTopMarginAfterList = 18;

        // vertical rhythm of the running text; turn this up for airier notes and down to tighten them
        // it is a minimum rather than a fixed value, see LineStackingStrategy below, so a heading keeps the
        // taller line box its own font size asks for
        private const double BodyLineHeight = 22;
        private const double BulletIndent = 16; // left inset of a list item
        private const double BulletHang = -11; // pulls the marker itself back out of that inset
        private const double InlineImageMaxWidth = 420; // an image in the running text never pushes the page wider

        // --- callout geometry ---
        private const double AlertRuleThickness = 3; // the vertical bar down the left of a callout
        private const double AlertInset = 14; // gap between that bar and the callout text


        // === callout colours ===

        // GitHub Primer, one pair per alert kind, the same values github.com renders these with
        // a status colour carries its meaning independently of the app theme, only light against dark changes
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

        // matched against heading text, case insensitive and as a substring, so an emoji in front does not matter
        private static readonly string[] DroppedSections = { "Installation" };


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
        // "**bold**" from being read as an italic star pair, and what keeps an image from matching the link arm
        // and leaving a stray "!" behind
        // underscore emphasis is deliberately absent: release notes carry identifiers like Some_Name_Here far more
        // often than they carry underscore italics, and treating those as markup mangles them
        private static readonly Regex InlinePattern = new(
            @"(?<image>!\[(?<imageAlt>[^\]]*)\]\((?<imageUrl>[^\s)]+)\))" +
            @"|(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^\s)]+)\))" +
            @"|(?<bold>\*\*(?<boldText>.+?)\*\*)" +
            @"|(?<code>`(?<codeText>[^`]+)`)" +
            @"|(?<url>https?://[^\s<>""]+)" +
            @"|(?<italic>\*(?<italicText>[^*\s][^*]*?)\*)",
            RegexOptions.Compiled);

        // markdown and the html form GitHub writes when an image is pasted into a release body; both count as
        // an image, and whichever appears first becomes the header image
        private static readonly Regex StandaloneImagePattern = new(
            @"!\[[^\]]*\]\((?<imageUrl>[^\s)]+)\)" +
            @"|<img[^>]*?\ssrc\s*=\s*[""'](?<imageUrl>[^""']+)[""'][^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HtmlImagePattern = new(@"<img[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a row of hashes with nothing after it; GitHub leaves these behind and they would render as literal text
        private static readonly Regex EmptyHeadingPattern = new(@"^\s*#{1,6}\s*$", RegexOptions.Compiled);

        // a release body ends on this line, and it has to survive whatever section removal happens above it
        private static readonly Regex ChangelogLinkPattern = new(@"^\s*\*\*Full Changelog\*\*", RegexOptions.Compiled | RegexOptions.IgnoreCase);


        // === public api ===

        // pulls the first image out of the body and hands it back separately, so a release can carry a header
        // image simply by starting its notes with one and it does not also appear in the running text
        //
        // PowerToys solves the same problem by keying on "Hero" in the alt text; taking whichever image comes
        // first means nothing has to be spelled a particular way when a release is written
        public static string ExtractLeadingImage(string markdown, out string imageUrl)
        {
            imageUrl = null;
            if (string.IsNullOrWhiteSpace(markdown)) return markdown;

            var match = StandaloneImagePattern.Match(markdown);
            if (!match.Success) return markdown;

            imageUrl = match.Groups["imageUrl"].Value;
            string rest = markdown.Remove(match.Index, match.Length);

            // whatever html images are left would otherwise show up as raw tags in the running text
            return HtmlImagePattern.Replace(rest, "");
        }

        // strips whole sections the dialog has no use for, from their heading down to the next heading of any
        // level or to the full changelog line, whichever comes first
        //
        // the installation steps belong on the release page, not in an app that is already installed
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

        // replaces whatever the target currently holds, so re-rendering into the same panel is safe
        public static void Render(Panel target, string markdown)
        {
            if (target == null) return;

            target.Children.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;

            // ActualTheme rather than the application theme, because the app sets its theme per element
            bool isDark = target.ActualTheme == ElementTheme.Dark;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var writer = new BlockWriter(target);

            // collects consecutive plain lines so a soft-wrapped paragraph stays one paragraph
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

                // a blank line, a horizontal rule and an empty heading all read as a break, and all three close
                // an open callout
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

                // anything that is not a quote line ends the callout it would otherwise be swallowed into
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

        // keeps the running RichTextBlock that ordinary paragraphs accumulate into, and swaps it for a callouts
        // own one while an alert is open, so the alert can sit in a Border that draws the rule down its side
        private sealed class BlockWriter
        {
            private readonly Panel _target;
            private RichTextBlock _current;
            private bool _inAlert;
            private bool _anyBlockWritten;
            private bool _lastWasListItem;

            public BlockWriter(Panel target) => _target = target;

            public bool IsInAlert => _inAlert;

            // only true until the very first block lands, which is what keeps a leading heading flush with the top
            public bool IsFirstBlock => !_anyBlockWritten;

            // what the paragraph after a list needs to know to bring its own top gap
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

                // the rule is the Borders own left edge, so it spans whatever height the callout ends up with
                _target.Children.Add(new Border
                {
                    BorderBrush = brush,
                    BorderThickness = new Thickness(AlertRuleThickness, 0, 0, 0),
                    Padding = new Thickness(AlertInset, 2, 0, 2),
                    Margin = new Thickness(0, 8, 0, 8),
                    Child = stack
                });

                // everything until EndAlert lands inside the callout instead of the running text
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

            // MaxHeight rather than BlockLineHeight: the line height set here then acts as a floor, so ordinary
            // text loosens up while a heading still gets the room its own size needs
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

            // the leading heading sits flush with the top of the dialog, everything after it gets its gap
            var paragraph = new Paragraph
            {
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, isFirstBlock ? 0 : HeadingTopMargin, 0, HeadingBottomMargin)
            };

            AppendInlines(paragraph, text);
            return paragraph;
        }

        // a quote inside a callout already sits behind the rule, so it drops the inset and the dimming it would
        // otherwise carry as an ordinary blockquote
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

        // resolved once per render rather than bound, which is fine because the dialog rebuilds its content every
        // time it opens, so a theme switch is picked up on the next open
        private static Brush ThemeBrush(string key) =>
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

        // an image inside the running text; the header image is pulled out before rendering, so this only ever
        // sees the extra ones a release body happens to contain
        // these load straight from their url and are therefore the one part of a release that stays blank without
        // a connection, unlike the header image, which ReleaseCatalog keeps on disk
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
