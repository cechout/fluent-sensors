using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;

using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // the release history, every published version from 1.0.0 onwards
    //
    // built in XAML rather than in code, unlike every other dialog in this app (see ShowInfoDialog and
    // ConfirmAction on the settings page): this one hosts a NavigationView driving a Frame, and assembling that
    // by hand would be far harder to read than the markup it replaces
    //
    // the cached history is shown the moment the dialog opens and the network refresh replaces it afterwards, so
    // opening this without a connection still shows everything that was ever fetched
    public sealed partial class ReleaseNotesDialog : ContentDialog
    {
        // === fields ===

        private List<ReleaseEntry> _releases = new List<ReleaseEntry>();

        // which release is on screen, so the slide can be sent in the direction the user actually moved
        private int _currentIndex = -1;

        // --- dialog geometry ---
        // the width is fixed at 85% of the main windows own minimum (WindowManager.MinWidth = 600), so the dialog
        // fits no matter how narrow the window has been dragged
        private const double DialogWidth = 510;
        private const double HeightFraction = 0.85; // how much of the window height the dialog may take
        private const double MaxDialogHeight = 740;
        private const double MinDialogHeight = 460;


        // === constructor ===

        public ReleaseNotesDialog()
        {
            this.InitializeComponent();
        }


        // === lifecycle ===

        private async void Dialog_Loaded(object sender, RoutedEventArgs e)
        {
            ResizeToWindow();

            // a ContentDialog cannot be dragged or resized by hand, so following the window is the next best thing
            if (this.XamlRoot != null) this.XamlRoot.Changed += OnXamlRootChanged;
            this.Closed += (_, _) =>
            {
                if (this.XamlRoot != null) this.XamlRoot.Changed -= OnXamlRootChanged;
            };

            var catalog = ReleaseCatalog.Instance;

            var cached = catalog.LoadCached();
            if (cached.Count > 0) Populate(cached);
            else SetBusy(true);

            var fresh = await catalog.RefreshAsync();

            SetBusy(false);

            if (fresh != null && fresh.Count > 0)
            {
                // only rebuild when the server actually changed something, otherwise the selection would reset
                // under the user for nothing
                if (fresh.Count != _releases.Count) Populate(fresh);
            }
            else if (_releases.Count == 0)
            {
                ShowStatus("The release history could not be loaded, and nothing has been saved yet");
            }
        }


        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeToWindow();

        // fixed width, height follows the window so the dialog never overflows a short display
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
            _currentIndex = -1;

            ReleaseNav.MenuItems.Clear();
            foreach (var release in _releases)
            {
                ReleaseNav.MenuItems.Add(new NavigationViewItem
                {
                    Content = release.Version,
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

            int index = _releases.IndexOf(release);

            // newer releases sit above older ones, so moving down the list reads as moving forward
            var effect = _currentIndex < 0 || index > _currentIndex
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;

            _currentIndex = index;

            ReleaseFrame.Navigate(
                typeof(ReleaseNotesPage),
                release,
                new SlideNavigationTransitionInfo { Effect = effect });
        }


        // === user interaction ===

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Hide();


        // === private helpers ===

        private void SetBusy(bool busy)
        {
            LoadingRing.IsActive = busy;
            LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            ReleaseFrame.Visibility = Visibility.Collapsed;
        }
    }
}
