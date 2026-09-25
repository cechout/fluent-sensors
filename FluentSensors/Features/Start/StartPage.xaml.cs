using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
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

        // the start header ships as one export per theme, picked in ApplyHeroImage
        private const string HeroImageLight = "ms-appx:///Assets/Pictures/start-header-light.png";
        private const string HeroImageDark = "ms-appx:///Assets/Pictures/start-header-dark.png";

        // uptime readout
        // polled four times a second rather than once, so the shown second never skips or lags behind the clock
        // the way a one second timer drifting against it would; the view model only raises when the text moves
        private static readonly TimeSpan UptimeTimerInterval = TimeSpan.FromMilliseconds(250);
        private DispatcherQueueTimer? _uptimeTimer;

        // copy version button
        private const string CopyGlyph = "\uE8C8";
        private const string CopiedGlyph = "\uE73E";
        private static readonly TimeSpan CopiedGlyphDuration = TimeSpan.FromSeconds(1.5); // how long the checkmark stays
        private DispatcherQueueTimer? _copiedGlyphTimer;

        // assigned before InitializeComponent runs, which is what the x:Bind expressions below need
        public StartViewModel ViewModel { get; } = new StartViewModel();


        // === constructor ===

        public StartPage()
        {
            this.InitializeComponent();

            VersionTextBlock.Text = UpdateService.VersionLabel(UpdateService.CurrentVersion);
            AboutVersionTextBlock.Text = UpdateService.VersionLabel(UpdateService.CurrentVersion);
        }


        // === lifecycle ===

        // the page is cached (NavigationCacheMode), so it survives navigating away and these two have to pair up
        // exactly or a second visit would subscribe twice
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateService.Instance.UpdateStateChanged += OnUpdateStateChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.HardwareIconColorsChanged += OnHardwareIconColorsChanged;
            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;
            this.ActualThemeChanged += OnActualThemeChanged;

            // whatever happened while the page was not listening
            ViewModel.RefreshUpdateState();
            ViewModel.RefreshIconBrushes();
            ApplyHeroImage();

            ViewModel.RefreshUptime();
            _uptimeTimer ??= CreateUptimeTimer();
            _uptimeTimer.Start();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            UpdateService.Instance.UpdateStateChanged -= OnUpdateStateChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            SettingsService.Instance.HardwareIconColorsChanged -= OnHardwareIconColorsChanged;
            AppStatusService.Instance.StatusUpdated -= OnStatusUpdated;
            this.ActualThemeChanged -= OnActualThemeChanged;

            _uptimeTimer?.Stop();
        }

        // only runs while the page is loaded; Page_Loaded catches the readout up the moment it comes back
        private DispatcherQueueTimer CreateUptimeTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = UptimeTimerInterval;
            timer.Tick += (s, e) => ViewModel.RefreshUptime();
            return timer;
        }

        // UpdateService already raises this from the UI thread, so there is nothing to dispatch here
        private void OnUpdateStateChanged() => ViewModel.RefreshUpdateState();

        // the badge colour is a plain brush rather than a theme resource, so it has to be rebuilt by hand when
        // the theme moves
        private void OnThemeChanged(string theme) => ViewModel.RefreshUpdateState();

        // the tile icons are the only thing on this page the hardware icon colour setting reaches
        private void OnHardwareIconColorsChanged() => ViewModel.RefreshIconBrushes();

        // ActualTheme rather than the ThemeChanged setting above, because it also moves when the app follows the
        // system and Windows switches underneath it, and because it only fires once the new theme is really applied
        // the snapshot tile icons are plain brushes, so they have to be rebuilt from here by hand
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            HardwareColorMode.IsDarkTheme = ActualTheme == ElementTheme.Dark;
            ViewModel.RefreshIconBrushes();
            ApplyHeroImage();
        }

        // AppStatusService fires from the UI thread as well, see its own Tick
        private void OnStatusUpdated(AppStatusData data) => ViewModel.ApplyStatus(data);

        // the header ships as two exports instead of one image that has to work on both backgrounds
        private void ApplyHeroImage()
        {
            string source = this.ActualTheme == ElementTheme.Dark ? HeroImageDark : HeroImageLight;

            // the export is 2560 wide against a 150 wide tile, and the compositor only bilinear filters, which at
            // that ratio reads far too few source pixels per drawn one and leaves hard aliased edges; decoding to
            // the tile size hands the reduction to the imaging stack instead, which reads all of them
            var bitmap = new BitmapImage
            {
                // Logical keeps this a DIP, so a scaled display still decodes to whole pixels
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = (int)HeroBorder.Width
            };

            // the decode starts as soon as a source is assigned, so the two above have to be set before this
            bitmap.UriSource = new Uri(source);

            HeroBrush.ImageSource = bitmap;
        }


        // === user interaction ===

        // the whole release history, not just the newest one; a dialog rather than a flyout because it carries a
        // navigation pane and a page of its own
        private async void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ReleaseNotesDialog
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };
            await dialog.ShowAsync();
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

        // copies the version in the exact shape the bug report form asks for, e.g. v1.6.0, and confirms it with a
        // checkmark in place of the copy glyph; a second click while it shows restarts the countdown
        private void CopyVersionButton_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText(UpdateService.VersionLabel(UpdateService.CurrentVersion));

            try
            {
                Clipboard.SetContent(package);
            }
            catch
            {
                // another app can hold the clipboard open for a moment; no checkmark then, the click can simply be
                // repeated
                return;
            }

            if (_copiedGlyphTimer == null)
            {
                _copiedGlyphTimer = DispatcherQueue.CreateTimer();
                _copiedGlyphTimer.Interval = CopiedGlyphDuration;
                _copiedGlyphTimer.IsRepeating = false;
                _copiedGlyphTimer.Tick += (s, args) => CopyVersionIcon.Glyph = CopyGlyph;
            }

            CopyVersionIcon.Glyph = CopiedGlyph;
            _copiedGlyphTimer.Stop();
            _copiedGlyphTimer.Start();
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


        // === x:Bind helpers ===

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InverseBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        public Visibility HasText(string value) =>
            string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
