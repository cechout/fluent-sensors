using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            if (string.IsNullOrWhiteSpace(body))
            {
                NotesHost.Visibility = Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                MarkdownRenderer.Render(NotesHost, body);
            }

            ShowHero(release.Version);
        }


        // === private helpers ===

        // the banner ships with the app rather than being pulled from the release body, which keeps it off the
        // network entirely and lets the release page on GitHub carry a rounded export while the app keeps a
        // square one
        //
        // HeroBorder starts collapsed and is only revealed once the image really decoded, so a release without
        // an asset, or one whose file is missing, simply has no header image
        private void ShowHero(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return;

            var bitmap = new BitmapImage(new Uri($"{HeroFolder}{UpdateService.VersionLabel(version).Replace('.', '-')}.png"));
            bitmap.ImageOpened += (_, _) => HeroBorder.Visibility = Visibility.Visible;

            HeroImage.Source = bitmap;
        }
    }
}
