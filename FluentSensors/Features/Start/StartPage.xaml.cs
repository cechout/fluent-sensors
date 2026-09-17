using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            await ShowCornerProbeAsync();
        }


        // --- teardown ladder: one step per build until the corners go square ---
        // stage 0 was a bare ContentDialog built here in code and it is round, so the fault is somewhere in what
        // ReleaseNotesDialog adds on top of that
        // everything below is built in code on purpose: that keeps the dialog a plain ContentDialog, so the one
        // thing this ladder cannot reach is being a XAML defined subclass, which is exactly what is left over if
        // every stage here stays round
        //
        // 0  bare dialog, title and a line of text          confirmed round
        // 1  our root grid and its header row                 confirmed round
        // 2  the fixed size on that grid                      confirmed round
        // 3  the navigation view in the lower row             confirmed round
        // 4  the three resource overrides                     confirmed round
        // 5  a frame in the navigation view, on the real release page
        // 6  the real ReleaseNotesDialog, which is the only thing left that a code built probe cannot be
        private const int ProbeStage = 5;

        private async Task ShowCornerProbeAsync()
        {
            // stage 6: no probe at all any more, the real type; everything it does differently from stage 5 comes
            // down to being declared in XAML as a ContentDialog subclass
            if (ProbeStage >= 6)
            {
                var real = new ReleaseNotesDialog { XamlRoot = this.XamlRoot };
                await real.ShowAsync();
                return;
            }

            var probe = new ContentDialog
            {
                Title = ProbeStage == 0 ? "Corner test" : null,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };

            if (ProbeStage == 0)
            {
                probe.Content = "Nothing in this dialog is ours.";
                await probe.ShowAsync();
                return;
            }

            // stage 4: the overrides the real dialog carried, set before the template is applied
            if (ProbeStage >= 4)
            {
                probe.Resources["ContentDialogPadding"] = new Thickness(0);
                probe.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
                probe.Resources["ContentDialogTopOverlay"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            // stage 1: the root grid, one auto row for the header and one star row for the rest
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Padding = new Thickness(24, 16, 14, 8), ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = "What's new",
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
            };
            header.Children.Add(headerText);

            var headerClose = new Button
            {
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new FontIcon { FontSize = 12, Glyph = "\uE711" },
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"]
            };
            Grid.SetColumn(headerClose, 1);
            header.Children.Add(headerClose);

            root.Children.Add(header);

            // stage 2: the fixed width and the height the window dictates
            if (ProbeStage >= 2)
            {
                var size = this.XamlRoot?.Size ?? default;
                if (size.Height > 0)
                {
                    root.Width = 510;
                    root.Height = Math.Clamp(size.Height * 0.85, 460, 740);
                }
            }

            // stage 3: the navigation view, pane length and display mode as the real one has them
            if (ProbeStage >= 3)
            {
                var nav = new NavigationView
                {
                    IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                    IsPaneOpen = true,
                    IsPaneToggleButtonVisible = false,
                    IsSettingsVisible = false,
                    OpenPaneLength = 120,
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    Content = BuildNavContent()
                };

                foreach (string version in new[] { "1.3.0", "1.2.0", "1.1.0" })
                {
                    nav.MenuItems.Add(new NavigationViewItem { Content = version });
                }

                Grid.SetRow(nav, 1);
                root.Children.Add(nav);
            }

            probe.Content = root;

            await probe.ShowAsync();
        }

        // stage 5 swaps the plain text for what the real dialog puts here: a Frame sitting on a real Page, with a
        // real release in it whenever the catalog has one cached
        private UIElement BuildNavContent()
        {
            if (ProbeStage < 5)
            {
                return new TextBlock
                {
                    Margin = new Thickness(28),
                    Text = "navigation view content",
                    TextWrapping = TextWrapping.Wrap
                };
            }

            var frame = new Frame();

            var cached = ReleaseCatalog.Instance.LoadCached();
            if (cached.Count > 0) frame.Navigate(typeof(ReleaseNotesPage), cached[0]);
            else frame.Navigate(typeof(ReleaseNotesPage));

            return frame;
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
