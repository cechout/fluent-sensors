using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

using FluentSensors.Common.UI;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // the release notes dialog:
    // every published version from 1.0.0; XAML unlike the other dialogs, since a NavigationView driving a
    // Frame reads far better as markup
    // the cached history shows at once and the refresh replaces it, so it works offline too
    public sealed partial class ReleaseNotesDialog : ContentDialog
    {
        // === fields ===

        private List<ReleaseEntry> _releases = new List<ReleaseEntry>();

        // --- dialog geometry ---
        // a fixed width below the 600 minimum of the main window, so it always fits
        private const double DialogWidth = 550;
        private const double HeightFraction = 0.85; // of the window height
        private const double MaxDialogHeight = 740;
        private const double MinDialogHeight = 460;


        // === constructor ===

        public ReleaseNotesDialog()
        {
            this.InitializeComponent();

            // its own popup layer, which the window reset never sees
            PointerFocusReset.Attach(this);
        }


        // === lifecycle ===

        private async void Dialog_Loaded(object sender, RoutedEventArgs e)
        {
            ResizeToWindow();

            // a ContentDialog cannot be resized by hand, so it follows the window
            if (this.XamlRoot != null) this.XamlRoot.Changed += OnXamlRootChanged;
            this.Closed += (_, _) =>
            {
                if (this.XamlRoot != null) this.XamlRoot.Changed -= OnXamlRootChanged;
            };

            var catalog = ReleaseCatalog.Instance;

            var cached = catalog.LoadCached();
            if (cached.Count > 0) Populate(cached);
            else SetBusy(true);

            // GitHub only when the cache misses a release, see EnsureCurrentAsync
            var fresh = await catalog.EnsureCurrentAsync();

            SetBusy(false);

            if (fresh != null && fresh.Count > 0)
            {
                // only on a real change, or the selection resets for nothing
                if (fresh.Count != _releases.Count) Populate(fresh);
            }
            else if (_releases.Count == 0)
            {
                ShowStatus(EmptyStatus());
            }
        }


        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeToWindow();

        // fixed width, the height follows the window
        private void ResizeToWindow()
        {
            var size = this.XamlRoot?.Size ?? default;
            if (size.Height <= 0) return;

            double height = Math.Clamp(size.Height * HeightFraction, MinDialogHeight, MaxDialogHeight);

            RootGrid.Width = DialogWidth;
            RootGrid.Height = height;
        }


        // === navigation ===

        private void Populate(IReadOnlyList<ReleaseEntry> releases)
        {
            _releases = releases.ToList();

            ReleaseNav.MenuItems.Clear();
            foreach (var release in _releases)
            {
                ReleaseNav.MenuItems.Add(new NavigationViewItem
                {
                    Content = UpdateService.VersionLabel(release.Version),
                    Tag = release
                });
            }

            StatusText.Visibility = Visibility.Collapsed;
            ReleaseFrame.Visibility = Visibility.Visible;

            if (ReleaseNav.MenuItems.Count > 0) ReleaseNav.SelectedItem = ReleaseNav.MenuItems[0];
        }

        private void ReleaseNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not ReleaseEntry release) return;

            // the recommended transition (the vertical entrance for a left pane), so a release
            // change reads like any page change
            ReleaseFrame.Navigate(typeof(ReleaseNotesPage), release, args.RecommendedNavigationTransitionInfo);
        }


        // === user interaction ===

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Hide();


        // === private helpers ===

        private void SetBusy(bool busy)
        {
            LoadingRing.IsActive = busy;
            LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        // only a fetch that did not come back gets here
        private static string EmptyStatus() =>
            "The release history could not be loaded, and nothing has been saved yet";

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            ReleaseFrame.Visibility = Visibility.Collapsed;
        }
    }
}
