using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

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
        private static readonly string HeroFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Releases");

        // kept for the banner, which renders once the page has a width (see Page_Loaded)
        private string _version = "";

        // the pixel width the banner was last rendered at, and a count so a slower render never lands after a newer one
        private int _heroPixelWidth;
        private int _heroRender;


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

        // the banner renders at the page width, which exists only after layout; a scale change is a new size too
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (XamlRoot != null) XamlRoot.Changed += XamlRoot_Changed;
            _ = RenderHeroAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (XamlRoot != null) XamlRoot.Changed -= XamlRoot_Changed;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => _ = RenderHeroAsync();

        private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => _ = RenderHeroAsync();


        // === private helpers ===

        // the banner ships with the app, off the network (GitHub carries a rounded export, the app a square one); a
        // missing file means no header
        //
        // scaled in one Fant pass (an average over every source pixel) to the physical width of the page and shown
        // 1:1 at that size; a decode width plus a stretch to the real, fractional size would filter twice, the
        // second time bilinear, and leave the text in the banner soft
        private async Task RenderHeroAsync()
        {
            if (string.IsNullOrWhiteSpace(_version) || XamlRoot == null || ActualWidth <= 0) return;

            double scale = XamlRoot.RasterizationScale;
            int pixelWidth = (int)Math.Ceiling(ActualWidth * scale); // ceiling, so no seam is left at the right edge
            if (pixelWidth == _heroPixelWidth) return;

            string path = Path.Combine(HeroFolder, $"{UpdateService.VersionLabel(_version).Replace('.', '-')}.png");
            if (!File.Exists(path)) return;

            _heroPixelWidth = pixelWidth;
            int render = ++_heroRender;

            try
            {
                using var stream = File.OpenRead(path).AsRandomAccessStream();
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                uint width = (uint)pixelWidth;
                uint height = (uint)Math.Max(1, Math.Round(width * (double)decoder.PixelHeight / decoder.PixelWidth));

                var transform = new BitmapTransform
                {
                    ScaledWidth = width,
                    ScaledHeight = height,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(bitmap);

                if (render != _heroRender) return;

                HeroImage.Source = source;
                HeroImage.Width = width / scale;
                HeroImage.Height = height / scale;
                HeroImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // an unreadable banner costs the header only; the next size change tries again
                Debug.WriteLine($"[ReleaseNotesPage] banner render failed: {ex.Message}");
                if (render == _heroRender) _heroPixelWidth = 0;
            }
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
    }
}
