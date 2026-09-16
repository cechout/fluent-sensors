using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;

using FluentSensors.Common.Markdown;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // one release inside the notes dialog; a real Page rather than a templated item, so the dialogs
    // NavigationView can drive it through a Frame and get the platforms own page transition
    public sealed partial class ReleaseNotesPage : Page
    {
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

            TitleText.Text = release.Name;
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

            // the header image comes out of the body first, so it does not also show up mid-text
            string body = MarkdownRenderer.ExtractLeadingImage(release.Notes, out string imageUrl);

            if (string.IsNullOrWhiteSpace(body))
            {
                NotesHost.Visibility = Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                MarkdownRenderer.Render(NotesHost, body);
            }

            _ = ShowHeroAsync(imageUrl);
        }


        // === private helpers ===

        // the cached copy is used the moment it exists, so reopening a release never waits on the network and an
        // offline app still shows the image it saw last time
        private async System.Threading.Tasks.Task ShowHeroAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return;

            var catalog = ReleaseCatalog.Instance;
            string path = catalog.CachedImagePath(imageUrl) ?? await catalog.EnsureImageAsync(imageUrl);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // loaded through a stream rather than a file uri, which BitmapImage does not reliably accept for
                // a path outside the app folder
                using var stream = File.OpenRead(path);

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

                HeroImage.Source = bitmap;
                HeroBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                // an unreadable or truncated cache file just means no header image for this release
            }
        }
    }
}
