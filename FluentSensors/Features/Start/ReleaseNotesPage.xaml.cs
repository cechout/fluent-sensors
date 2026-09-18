using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;

using FluentSensors.Common.Markdown;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // one release inside the notes dialog; a real Page rather than a templated item, so the dialogs
    // NavigationView can drive it through a Frame and get the platforms own page transition
    public sealed partial class ReleaseNotesPage : Page
    {
        // === fields ===

        // every release ships its own banner under Assets/Releases, named after the version with dots as dashes
        private const string HeroFolder = "ms-appx:///Assets/Releases/";

        // width over height of the banner currently shown, so the border can derive its own height from it
        private double _heroAspect;

        // the release on show, held because the banner is only loaded once this page has a width, see Page_Loaded
        private string _version = "";


        // === constructor ===

        public ReleaseNotesPage()
        {
            this.InitializeComponent();
        }


        // === navigation ===

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not ReleaseEntry release) return;

            DateText.Text = release.PublishedAt == DateTimeOffset.MinValue
                ? ""
                : release.PublishedAt.ToLocalTime().ToString("dd.MM.yyyy");

            if (Uri.TryCreate(release.ReleaseUrl, UriKind.Absolute, out var releaseUri))
            {
                GitHubLink.NavigateUri = releaseUri;
            }
            else
            {
                GitHubLink.Visibility = Visibility.Collapsed;
            }

            // the leading image is dropped from the body so it does not also show up mid-text; the copy in the
            // notes is never rendered, ShowHero takes the shipped asset instead
            string body = MarkdownRenderer.ExtractLeadingImage(release.Notes, out _);
            body = MarkdownRenderer.DropSections(body);

            // the closing changelog line leaves the body as well, it has a row of its own under the notes
            body = MarkdownRenderer.ExtractChangelogLink(body, out string changelogUrl);

            if (string.IsNullOrWhiteSpace(body))
            {
                NotesHost.Visibility = Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                MarkdownRenderer.Render(NotesHost, body);
            }

            ShowChangelog(changelogUrl);

            _version = release.Version;
        }

        // the banner waits for this rather than loading in OnNavigatedTo, because it decodes to the width this
        // page ends up with and that width only exists once the page has been laid out
        private void Page_Loaded(object sender, RoutedEventArgs e) => ShowHero();


        // === private helpers ===

        // the banner ships with the app rather than being pulled from the release body, which keeps it off the
        // network entirely and lets the release page on GitHub carry a rounded export while the app keeps a
        // square one
        //
        // HeroBorder starts collapsed and is only revealed once the image really decoded, so a release without
        // an asset, or one whose file is missing, simply has no header image
        private void ShowHero()
        {
            if (string.IsNullOrWhiteSpace(_version)) return;

            // the banner ships far wider than the page ever draws it, and the compositor only bilinear filters,
            // which at that ratio reads too few source pixels per drawn one and leaves hard aliased edges
            var bitmap = new BitmapImage
            {
                // Logical keeps this a DIP, and a width of zero simply decodes at natural size
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = (int)this.ActualWidth
            };

            bitmap.ImageOpened += (_, _) =>
            {
                if (bitmap.PixelHeight <= 0) return;

                _heroAspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                SetHeroHeight();

                HeroBorder.Visibility = Visibility.Visible;
            };

            // last, the decode starts as soon as a source is assigned and the width above has to be set by then
            bitmap.UriSource = new Uri($"{HeroFolder}{UpdateService.VersionLabel(_version).Replace('.', '-')}.png");

            HeroBorder.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }

        // the label is plain XAML so it follows the theme; only the address and its button text come from here
        private void ShowChangelog(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

            ChangelogLink.NavigateUri = uri;
            ChangelogLink.Content = CompareLabel(uri);

            ChangelogRow.Visibility = Visibility.Visible;
        }

        // ".../compare/v1.2.0...v1.3.0" reads as the range itself; anything that is not a compare url falls back
        // to naming the host, so the button never ends up blank
        private static string CompareLabel(Uri uri)
        {
            string last = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "";

            return last.Length > 0 ? Uri.UnescapeDataString(last) : uri.Host;
        }

        private void HeroBorder_SizeChanged(object sender, SizeChangedEventArgs e) => SetHeroHeight();

        // a border with no child has no height of its own, so the banners aspect supplies one; the half pixel
        // guard is what keeps setting the height from feeding its own SizeChanged back in
        private void SetHeroHeight()
        {
            if (_heroAspect <= 0 || HeroBorder.ActualWidth <= 0) return;

            double target = HeroBorder.ActualWidth / _heroAspect;
            if (Math.Abs(HeroBorder.Height - target) < 0.5) return;

            HeroBorder.Height = target;
        }
    }
}
