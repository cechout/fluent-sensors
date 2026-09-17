using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        //
        // internal rather than private while the corner probe runs: the probe calls it before ShowAsync, to tell
        // apart a dialog that opens at its final size from one that is resized once it is already open
        internal void ResizeToWindow()
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

            // --- teardown diagnostic: comes out once the dialog is whole again ---
            // anything that throws while the page is being built surfaces at the line that sets the selection,
            // which names neither the cause nor the file; this puts the real message on screen instead
            try
            {
                // the NavigationView hands out the transition its own display mode calls for, so a release change
                // reads exactly like a page change anywhere else in the app rather than like a hand rolled slide
                ReleaseFrame.Navigate(typeof(ReleaseNotesPage), release, args.RecommendedNavigationTransitionInfo);
            }
            catch (Exception ex)
            {
                ShowStatus($"{ex.GetType().Name}\n{ex.Message}");
            }
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
