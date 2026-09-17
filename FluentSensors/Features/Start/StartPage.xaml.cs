using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

using FluentSensors.Core;
using FluentSensors.Core.Update;
using FluentSensors.Features.Update;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Start
{
    // the apps entry point: update state, a snapshot of the machine it runs on, this apps own live status, and
    // the about/licence block that used to sit at the bottom of the settings page
    //
    // which page a launch actually lands on is a setting, see StartupPage and MainWindows splash reveal
    public sealed partial class StartPage : Page
    {
        // === fields ===

        // assigned before InitializeComponent runs, which is what the x:Bind expressions below need
        public StartViewModel ViewModel { get; } = new StartViewModel();


        // === constructor ===

        public StartPage()
        {
            this.InitializeComponent();

            VersionTextBlock.Text = UpdateService.CurrentVersion;
            AboutVersionTextBlock.Text = $"Version {UpdateService.CurrentVersion}";
        }


        // === lifecycle ===

        // the page is cached (NavigationCacheMode), so it survives navigating away and these two have to pair up
        // exactly or a second visit would subscribe twice
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateService.Instance.UpdateStateChanged += OnUpdateStateChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;

            // whatever happened while the page was not listening
            ViewModel.RefreshUpdateState();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            UpdateService.Instance.UpdateStateChanged -= OnUpdateStateChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            AppStatusService.Instance.StatusUpdated -= OnStatusUpdated;
        }

        // UpdateService already raises this from the UI thread, so there is nothing to dispatch here
        private void OnUpdateStateChanged() => ViewModel.RefreshUpdateState();

        // the badge colour is a plain brush rather than a theme resource, so it has to be rebuilt by hand when
        // the theme moves
        private void OnThemeChanged(string theme) => ViewModel.RefreshUpdateState();

        // AppStatusService fires from the UI thread as well, see its own Tick
        private void OnStatusUpdated(AppStatusData data) => ViewModel.ApplyStatus(data);


        // === user interaction ===

        // the whole release history, not just the newest one; a dialog rather than a flyout because it carries a
        // navigation pane and a page of its own
        private async void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            // --- teardown: bare dialog, nothing of ours left ---
            // the release dialog is square down to the close button the template itself draws, which no markup of
            // ours ever touches, so this opens a ContentDialog with no custom type, no custom size and no custom
            // content at all
            // round here means the fault is inside ReleaseNotesDialog and goes back in line by line; square here
            // means it was never that file and the hunt moves out of it
            var probe = new ContentDialog
            {
                Title = "Corner test",
                Content = "Nothing in this dialog is ours.",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };

            await probe.ShowAsync();
        }

        // one button, four jobs, decided by whatever state the service is in
        private async void UpdateStatusButton_Click(object sender, RoutedEventArgs e)
        {
            var service = UpdateService.Instance;

            switch (service.UiState)
            {
                case UpdateUiState.UpdateAvailable:
                    await ShowUpdateDialogAsync(service.Latest);
                    break;

                case UpdateUiState.Skipped:
                    // the way back out of a skip; without this the persisted version would stay unreachable until
                    // the release after it
                    service.ClearSkippedVersion();
                    await ShowUpdateDialogAsync(service.Latest);
                    break;

                case UpdateUiState.StoreManaged:
                    // the button is disabled in this state anyway, this is just the matching arm
                    break;

                default:
                    await service.CheckAsync();
                    break;
            }
        }

        private async Task ShowUpdateDialogAsync(UpdateInfo? info)
        {
            if (info == null) return;

            await UpdateDialog.ShowAsync(this.XamlRoot, info);
        }

        // the settings json files live somewhere else in a portable build, so the path is asked for rather than
        // assumed
        // the sensor count under each snapshot tile; opens the sensor list with exactly that hardware group
        // expanded and every other one closed
        private void SensorCount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SystemSnapshotEntry entry) return;

            MainWindow.CurrentInstance?.OpenSensorsForHardware(entry.MatchedHardwareNames);
        }

        private void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(PersistenceService.Instance.RootFolder);
        }

        private static void OpenPath(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;

            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { /* no explorer reachable, and this page has nowhere to report that to */ }
        }


        // === x:Bind helpers ===

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InverseBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        public Visibility HasText(string value) =>
            string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
