using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

using FluentSensors.Common;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Core;
using FluentSensors.Core.Update;
using FluentSensors.Features.Update;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Start
{
    // the start page:
    // update state, a snapshot of the machine, the live status of this app, and the about and licence block; (the
    // landing page is a setting, see StartupPage)
    public sealed partial class StartPage : Page
    {
        // === fields ===

        // one header export per theme, see ApplyHeroImage
        private const string HeroImageLight = "ms-appx:///Assets/Pictures/start-header-light.png";
        private const string HeroImageDark = "ms-appx:///Assets/Pictures/start-header-dark.png";

        // uptime readout; polled four times a second, so the shown second never skips like a drifting one second timer
        // would (the view model only raises on a text change)
        private static readonly TimeSpan UptimeTimerInterval = TimeSpan.FromMilliseconds(250);
        private DispatcherQueueTimer? _uptimeTimer;

        // copy version button
        private const string CopyGlyph = "\uE8C8";
        private const string CopiedGlyph = "\uE73E";
        private static readonly TimeSpan CopiedGlyphDuration = TimeSpan.FromSeconds(1.5); // the checkmark
        private DispatcherQueueTimer? _copiedGlyphTimer;

        // assigned before InitializeComponent, for the x:Bind expressions
        public StartViewModel ViewModel { get; } = new StartViewModel();

        // the store review link, store build only
        public bool IsStoreBuild => AppDistribution.IsPackaged;


        // === constructor ===

        public StartPage()
        {
            this.InitializeComponent();

            VersionTextBlock.Text = UpdateService.VersionLabel(UpdateService.CurrentVersion);
            AboutVersionTextBlock.Text = UpdateService.VersionLabel(UpdateService.CurrentVersion);
        }


        // === lifecycle ===

        // the page is cached, so these two pair up exactly or a second visit subscribes twice
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateService.Instance.UpdateStateChanged += OnUpdateStateChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.HardwareIconColorsChanged += OnHardwareIconColorsChanged;
            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;
            this.ActualThemeChanged += OnActualThemeChanged;

            // what happened while not listening
            ViewModel.RefreshUpdateState();
            // (a theme switch on the settings page happens while this page is unloaded and does not move
            // HardwareColorMode.IsDarkTheme)
            HardwareColorMode.IsDarkTheme = ActualTheme == ElementTheme.Dark;
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

        // runs while loaded only; Page_Loaded catches up on return
        private DispatcherQueueTimer CreateUptimeTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = UptimeTimerInterval;
            timer.Tick += (s, e) => ViewModel.RefreshUptime();
            return timer;
        }

        // already on the UI thread
        private void OnUpdateStateChanged() => ViewModel.RefreshUpdateState();

        // the badge colour is a plain brush, rebuilt on a theme change
        private void OnThemeChanged(string theme) => ViewModel.RefreshUpdateState();

        // the tile icons, the only thing here the icon colour setting reaches
        private void OnHardwareIconColorsChanged() => ViewModel.RefreshIconBrushes();

        // ActualTheme, not the setting: it also moves when Windows switches under a following app, and only once the
        // theme is applied; the tile icons are plain brushes
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            HardwareColorMode.IsDarkTheme = ActualTheme == ElementTheme.Dark;
            ViewModel.RefreshIconBrushes();
            ApplyHeroImage();
        }

        // on the UI thread as well, see AppStatusService.Tick
        private void OnStatusUpdated(AppStatusData data) => ViewModel.ApplyStatus(data);

        // two exports, not one image for both backgrounds
        private void ApplyHeroImage()
        {
            string source = this.ActualTheme == ElementTheme.Dark ? HeroImageDark : HeroImageLight;

            // decoded to the tile size; the compositor filters bilinear only, at 2560 to 150 that aliases hard
            var bitmap = new BitmapImage
            {
                // Logical keeps it a DIP, so a scaled display decodes to whole pixels
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = (int)HeroBorder.Width
            };

            // the decode starts with the source, so the two above come first
            bitmap.UriSource = new Uri(source);

            HeroBrush.ImageSource = bitmap;
        }


        // === user interaction ===

        // the whole release history; a dialog, it carries a navigation pane and a page
        private async void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ReleaseNotesDialog
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };
            await dialog.ShowAsync();
        }

        // one button, three jobs, by service state
        private async void UpdateStatusButton_Click(object sender, RoutedEventArgs e)
        {
            var service = UpdateService.Instance;

            switch (service.UiState)
            {
                case UpdateUiState.UpdateAvailable:
                    await ShowUpdateDialogAsync(service.Latest);
                    break;

                case UpdateUiState.Skipped:
                    // the way back out of a skip
                    service.ClearSkippedVersion();
                    await ShowUpdateDialogAsync(service.Latest);
                    break;

                default:
                    await service.CheckAsync();
                    break;
            }
        }

        private async void StoreReviewLink_Click(object sender, RoutedEventArgs e) =>
            await AppDistribution.OpenStoreReviewAsync();

        private async Task ShowUpdateDialogAsync(UpdateInfo? info)
        {
            if (info == null) return;

            await UpdateDialog.ShowAsync(this.XamlRoot, info);
        }

        // copies the version the way the bug report form asks for it (v1.6.0), confirmed by a
        // checkmark; a second click restarts it
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
                // another app can hold the clipboard for a moment; no checkmark, the click can be repeated
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

        // the sensor count under a snapshot tile; the sensor list with that group open and every other one closed
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
