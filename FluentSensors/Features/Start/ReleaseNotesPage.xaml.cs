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
    // one release in the notes dialog; a Page, so the NavigationView drives it through a Frame
    // with the platform transition
    public sealed partial class ReleaseNotesPage : Page
    {
        // === fields ===

        // one banner per release under Assets/Releases, the version with dashes for dots
        private const string HeroFolder = "ms-appx:///Assets/Releases/";

        // width over height of the shown banner, for the border height
        private double _heroAspect;

        // kept for the banner, which loads once the page has a width (see Page_Loaded)
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

            // the leading image leaves the body; ShowHero takes the shipped asset instead
            string body = MarkdownRenderer.ExtractLeadingImage(release.Notes, out _);
            body = MarkdownRenderer.DropSections(body);

            // the changelog line too, it has its own row
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

        // the banner decodes to the page width, which exists only after layout
        private void Page_Loaded(object sender, RoutedEventArgs e) => ShowHero();


        // === private helpers ===

        // the banner ships with the app, off the network (GitHub carries a rounded export, the app a square one);
        // HeroBorder shows only once the image decoded, a missing file means no header
        private void ShowHero()
        {
            if (string.IsNullOrWhiteSpace(_version)) return;

            // decoded to the page width; the compositor filters bilinear only and would alias the wide export
            var bitmap = new BitmapImage
            {
                // Logical keeps it a DIP; a width of zero decodes at natural size
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

            // last, the decode starts with the source
            bitmap.UriSource = new Uri($"{HeroFolder}{UpdateService.VersionLabel(_version).Replace('.', '-')}.png");

            HeroBorder.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }

        // the label is XAML and follows the theme; the address and button text come from here
        private void ShowChangelog(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

            ChangelogLink.NavigateUri = uri;
            ChangelogLink.Content = CompareLabel(uri);

            ChangelogRow.Visibility = Visibility.Visible;
        }

        // ".../compare/v1.2.0...v1.3.0" reads as the range; anything else names the host, never blank
        private static string CompareLabel(Uri uri)
        {
            string last = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "";

            return last.Length > 0 ? Uri.UnescapeDataString(last) : uri.Host;
        }

        private void HeroBorder_SizeChanged(object sender, SizeChangedEventArgs e) => SetHeroHeight();

        // a childless border has no height, the banner aspect gives one; the half pixel guard stops a SizeChanged loop
        private void SetHeroHeight()
        {
            if (_heroAspect <= 0 || HeroBorder.ActualWidth <= 0) return;

            double target = HeroBorder.ActualWidth / _heroAspect;
            if (Math.Abs(HeroBorder.Height - target) < 0.5) return;

            HeroBorder.Height = target;
        }
    }
}
