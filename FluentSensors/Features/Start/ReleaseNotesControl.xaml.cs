using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

using FluentSensors.Common.Markdown;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // the contents of the start pages release notes flyout: what changed, and nothing else
    //
    // strictly a reader, it never offers to install anything; that is the neighbouring update button, which is
    // also why this one works the same on a store build where the updater itself never runs
    public sealed partial class ReleaseNotesControl : UserControl
    {
        // === fields ===

        // the notes are fetched on the first open and kept for the session, so reopening the flyout is instant
        // and does not spend another api call on a rate limited endpoint
        private bool _hasLoaded;


        // === constructor ===

        public ReleaseNotesControl()
        {
            this.InitializeComponent();
        }


        // === public api ===

        // driven by the flyouts Opening event rather than by construction, which is what keeps a store build from
        // reaching GitHub unless someone actually asks for the notes
        public async Task LoadAsync()
        {
            if (_hasLoaded) return;

            SetBusy(true);

            UpdateInfo? release = null;
            try
            {
                release = await UpdateService.Instance.EnsureLatestReleaseAsync();
            }
            catch
            {
                // EnsureLatestReleaseAsync already swallows the usual network failures; this is the belt and
                // braces case, a flyout should never take the app down
            }

            SetBusy(false);

            if (release == null)
            {
                ShowStatus("The release notes could not be loaded, check your connection and try again");
                return;
            }

            HeadingText.Text = $"What's new in {release.Version}";
            SubheadingText.Text = string.Equals(release.Version, UpdateService.CurrentVersion, StringComparison.Ordinal)
                ? "The version you are running"
                : $"You are running {UpdateService.CurrentVersion}";

            if (!string.IsNullOrEmpty(release.ReleaseUrl))
            {
                ReleaseLink.NavigateUri = new Uri(release.ReleaseUrl);
            }

            if (string.IsNullOrWhiteSpace(release.Notes))
            {
                ShowStatus("This release was published without notes");
                return;
            }

            MarkdownRenderer.Render(NotesBlock, release.Notes);
            _hasLoaded = true;
        }


        // === private helpers ===

        private void SetBusy(bool busy)
        {
            LoadingRing.IsActive = busy;
            LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Visibility = Visibility.Collapsed;
            NotesScroller.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        }

        // a failure is deliberately not cached, so the next open retries instead of showing the same message for
        // the rest of the session
        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            NotesScroller.Visibility = Visibility.Collapsed;
        }
    }
}
