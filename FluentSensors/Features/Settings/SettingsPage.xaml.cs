using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

using FluentSensors.Persistence.Services;
using FluentSensors.Persistence.Models;
using FluentSensors.Core;
using FluentSensors.Core.Startup;
using FluentSensors.Core.Taskbar;
using FluentSensors.Common.Csv;
using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Features.TaskbarWidget;


namespace FluentSensors.Features.Settings
{
    public sealed partial class SettingsPage : Page
    {
        // keeps the handlers quiet during initialization
        private bool _isLoading = true;

        // while this page writes a status readout setting itself, see OnStatusReadoutChanged
        private bool _isWritingStatusReadout;

        // the gear button of the taskbar flyout asked for the taskbar section before the page was loaded
        private bool _isTaskbarSectionScrollPending;


        // === constructor ===

        public SettingsPage()
        {
            this.InitializeComponent();

            // the saved selections
            RestoreThemeSelection();
            RestoreLanguageSelection();
            RestoreTechnicalTermsSelection();
            RestoreIntervalSelection();
            RestoreMinimizeToTraySelection();
            RestoreStartupSelection();
            RestoreStatusReadoutSelection();
            RestoreCsvFormatSelection();
            RestoreGraphLineStyleSelection();
            RestoreGraphFillFadeSelection();
            RestoreDataUnitSelection();
            RestoreHardwareIconColorsSelection();

            // the time range lists the pickers share; from code, the restores below run before x:Bind would
            PerformanceGraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.Performance;
            PerformanceCpuExtendedGraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.PerformanceExtended;
            PerformanceGpuExtendedGraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.PerformanceExtended;
            GraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.Widget;
            TaskbarGraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.Taskbar;
            TaskbarFlyoutGraphTimeSpanComboBox.ItemsSource = GraphTimeRanges.Taskbar;

            RestorePerformanceGraphTimeSpanSelection();

            RestoreBackgroundMaterialSettings();
            RestoreGraphColorSettings();
            RestoreGraphTimeSpanSelection();

            RestoreTaskbarBackgroundMaterialSettings();
            RestoreTaskbarGraphColorSettings();
            RestoreTaskbarGraphBackgroundSourceSelection();
            RestoreTaskbarGraphTimeSpanSelection();
            RestoreTaskbarGraphWidthSelection();
            RestoreTaskbarSideSlotSelection();
            RestoreTaskbarFlyoutAlignmentSelection();
            RestoreTaskbarFlyoutGraphSelection();
            ApplyFlyoutShortcutButton();
            RestoreLockWidgetPositionSelection();

            ShowAppDataFolderPath();


            // color picker callbacks
            WidgetBackgroundColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                WidgetBackgroundColorPicker_SelectedColorChanged);

            GraphColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                GraphColorPicker_SelectedColorChanged);

            TaskbarBackgroundColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                TaskbarBackgroundColorPicker_SelectedColorChanged);

            TaskbarGraphColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                TaskbarGraphColorPicker_SelectedColorChanged);

            _isLoading = false;
        }


        // === page lifecycle ===

        // the title bar toggle writes the same master setting, so the controls can go stale in the navigation cache
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.StatusReadoutChanged += OnStatusReadoutChanged;
            OnStatusReadoutChanged();

            // the per edge settings follow the taskbar even while the taskbar widget is closed
            SettingsService.Instance.ActiveTaskbarEdgeChanged += OnActiveTaskbarEdgeChanged;
            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            if (primaryTaskbar != null)
            {
                SettingsService.Instance.ActiveTaskbarEdge = primaryTaskbar.Edge.ToString();
            }
            OnActiveTaskbarEdgeChanged(SettingsService.Instance.ActiveTaskbarEdge);

            // the time range pickers under the graphs, in the widget and in the flyout write the same settings
            SettingsService.Instance.PerformanceGraphTimeSpanChanged += OnTimeRangesChanged;
            SettingsService.Instance.GraphTimeSpanChanged += OnTimeRangeChanged;
            SettingsService.Instance.TaskbarGraphTimeSpanChanged += OnTimeRangeChanged;
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged += OnTimeRangeChanged;
            OnTimeRangesChanged();

            if (_isTaskbarSectionScrollPending)
            {
                _isTaskbarSectionScrollPending = false;
                QueueScrollToTaskbarSection();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.StatusReadoutChanged -= OnStatusReadoutChanged;
            SettingsService.Instance.ActiveTaskbarEdgeChanged -= OnActiveTaskbarEdgeChanged;
            SettingsService.Instance.PerformanceGraphTimeSpanChanged -= OnTimeRangesChanged;
            SettingsService.Instance.GraphTimeSpanChanged -= OnTimeRangeChanged;
            SettingsService.Instance.TaskbarGraphTimeSpanChanged -= OnTimeRangeChanged;
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged -= OnTimeRangeChanged;
        }

        // the values of the active taskbar edge; the side cards only on the left and right edge
        private void OnActiveTaskbarEdgeChanged(string edge)
        {
            TaskbarPositionText.Text = AppStrings.Get($"Settings_TaskbarEdge{edge}");

            var sideVisibility = edge is "Left" or "Right" ? Visibility.Visible : Visibility.Collapsed;
            TaskbarSideGraphDirectionCard.Visibility = sideVisibility;
            TaskbarSideTitleLinesCard.Visibility = sideVisibility;

            _isLoading = true;
            RestoreTaskbarGraphTimeSpanSelection();
            RestoreTaskbarGraphWidthSelection();
            RestoreTaskbarSideSlotSelection();
            RestoreTaskbarFlyoutAlignmentSelection();
            _isLoading = false;
        }

        // a pick in a time range picker; (a write from here echoes back as a no-op)
        private void OnTimeRangesChanged()
        {
            _isLoading = true;
            RestorePerformanceGraphTimeSpanSelection();
            RestoreGraphTimeSpanSelection();
            RestoreTaskbarGraphTimeSpanSelection();
            RestoreTaskbarFlyoutGraphSelection();
            _isLoading = false;
        }

        private void OnTimeRangeChanged(double newTimeSpanSeconds) => OnTimeRangesChanged();

        // the taskbar section at the top, for the gear button of the taskbar flyout; a page that is not loaded yet
        // (first visit, or back from the navigation cache) scrolls once it is
        public void ScrollToTaskbarSection()
        {
            if (IsLoaded) QueueScrollToTaskbarSection();
            else _isTaskbarSectionScrollPending = true;
        }

        // at Low priority, after the layout pass; the target reaches up over the header margin, so the gap above the
        // heading stays in view
        private void QueueScrollToTaskbarSection()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                double gap = TaskbarSectionHeader.Margin.Top;
                TaskbarSectionHeader.StartBringIntoView(new BringIntoViewOptions
                {
                    TargetRect = new Rect(0, -gap, TaskbarSectionHeader.ActualWidth, TaskbarSectionHeader.ActualHeight + gap),
                    VerticalAlignmentRatio = 0,
                    AnimationDesired = true
                });
            });
        }

        // for writes from outside this page; a write from here echoes back and would reset the control mid handler
        private void OnStatusReadoutChanged()
        {
            if (_isWritingStatusReadout) return;

            _isLoading = true;
            RestoreStatusReadoutSelection();
            _isLoading = false;
        }


        // === general settings ===

        // theme
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string themeTag = selectedItem.Tag?.ToString();

                SettingsService.Instance.AppTheme = themeTag;

                // the window root
                if (this.XamlRoot?.Content is FrameworkElement rootElement)
                {
                    // tag to ElementTheme
                    rootElement.RequestedTheme = themeTag switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }

        private void RestoreThemeSelection()
        {
            string currentTheme = SettingsService.Instance.AppTheme;

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // language; saved right away, it applies on the next start
        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

            SettingsService.Instance.AppLanguage = tag;
            UpdateLanguageRestartBar();
        }

        private void RestoreLanguageSelection()
        {
            if (LanguageComboBox.Items.Count == 1)
            {
                foreach (var (tag, name) in AppLanguage.Supported)
                {
                    LanguageComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
                }
            }

            string current = SettingsService.Instance.AppLanguage;
            SelectByTag(LanguageComboBox, AppLanguage.IsSupported(current) ? current : AppLanguage.SystemDefault);
        }

        // technical terms in english; like the language, saved right away and applied on the next start
        private void TechnicalTermsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsService.Instance.TechnicalTermsInEnglish = TechnicalTermsToggle.IsOn;
            UpdateLanguageRestartBar();
        }

        // the resource language actually in use, not the setting; Default can resolve to english too
        private void RestoreTechnicalTermsSelection()
        {
            TechnicalTermsToggle.IsOn = SettingsService.Instance.TechnicalTermsInEnglish;
            TechnicalTermsCard.IsEnabled = AppStrings.Get("App_LanguageTag") != "en-US";
            UpdateLanguageRestartBar();
        }

        // open while either choice differs from what this process started with; turning a choice back closes it
        private void UpdateLanguageRestartBar()
        {
            var settings = SettingsService.Instance;
            bool languageChanged = AppLanguage.Normalize(settings.AppLanguage) != AppLanguage.StartupSetting;
            bool termsChanged = settings.TechnicalTermsInEnglish != AppTerms.StartedInEnglish;

            string message = AppStrings.Get(
                languageChanged && termsChanged ? "Settings_RestartPendingBoth"
                : languageChanged ? "Settings_RestartPendingLanguage"
                : "Settings_RestartPendingTerms");
            bool open = languageChanged || termsChanged;

            // announced like an InfoBar would, once per new message
            if (open && (LanguageRestartBar.Visibility != Visibility.Visible || LanguageRestartText.Text != message))
            {
                LanguageRestartText.Text = message;
                FrameworkElementAutomationPeer.FromElement(LanguageRestartText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            LanguageRestartBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        // flushed before the restart, the new process reads the settings before the old one exits
        private void LanguageRestartButton_Click(object sender, RoutedEventArgs e)
        {
            PersistenceService.Instance.FlushAll();
            RestartApp();
        }

        // update interval
        private void IntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int newIntervalMs))
                {
                    // applies at runtime
                    HardwareMonitorService.Instance.UpdateIntervalMs = newIntervalMs;
                    SettingsService.Instance.SaveDebounced();
                }
            }
        }

        private void RestoreIntervalSelection()
        {
            int currentInterval = HardwareMonitorService.Instance.UpdateIntervalMs;

            foreach (ComboBoxItem item in IntervalComboBox.Items)
            {
                if (item.Tag?.ToString() == currentInterval.ToString())
                {
                    IntervalComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // minimize to tray
        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.MinimizeToTray = MinimizeToTrayToggle.IsOn;
        }

        private void RestoreMinimizeToTraySelection()
        {
            MinimizeToTrayToggle.IsOn = SettingsService.Instance.MinimizeToTray;
        }


        // startup

        // the scheduled task decides, not the setting, so a task removed by hand shows as off; the portable build has
        // no autostart (the task would outlive its folder)
        private void RestoreStartupSelection()
        {
            var settings = SettingsService.Instance;

            StartMinimizedToggle.IsOn = settings.StartMinimizedToTray;
            CheckUpdatesToggle.IsOn = settings.CheckUpdatesOnStartup;

            // above the early return below; the landing page has nothing to do with autostart
            string currentStartupPage = settings.StartupPage.ToString();
            foreach (ComboBoxItem item in StartupPageComboBox.Items)
            {
                if (item.Tag?.ToString() == currentStartupPage)
                {
                    StartupPageComboBox.SelectedItem = item;
                    break;
                }
            }

            if (!WinAutostartService.IsSupported)
            {
                RunOnStartupCard.Description = AppStrings.Get("Settings_RunOnStartupPortable");
                RunOnStartupToggle.Visibility = Visibility.Collapsed;
                DelayStartupCard.Visibility = Visibility.Collapsed;
                UpdateStartupCardStates();
                return;
            }

            // a task on a moved exe is repaired at app start, not here, so it is fixed without visiting this page
            bool taskExists = WinAutostartService.IsEnabled();
            if (settings.RunOnStartup != taskExists) settings.RunOnStartup = taskExists;

            RunOnStartupToggle.IsOn = taskExists;
            DelayStartupToggle.IsOn = settings.DelayStartup;
            UpdateStartupCardStates();
        }

        // both rows only matter while Windows launches the app: a delay needs the task, start minimized is about the
        // sign-in launch (see MainWindow.StartsHiddenInTray); the portable build has neither
        private void UpdateStartupCardStates()
        {
            bool autostartActive = WinAutostartService.IsSupported && RunOnStartupToggle.IsOn;

            DelayStartupCard.IsEnabled = autostartActive;
            StartMinimizedCard.IsEnabled = autostartActive;
        }

        private async void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            bool wanted = RunOnStartupToggle.IsOn;
            if (WinAutostartService.Apply(wanted, DelayStartupToggle.IsOn))
            {
                SettingsService.Instance.RunOnStartup = wanted;
                UpdateStartupCardStates();
                return;
            }

            // the task scheduler refused; the switch goes back
            _isLoading = true;
            RunOnStartupToggle.IsOn = !wanted;
            _isLoading = false;
            UpdateStartupCardStates();

            await ShowInfoDialog(AppStrings.Get("Settings_StartupFailedTitle"), AppStrings.Get("Settings_StartupFailedMessage"));
        }

        private async void DelayStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            bool wanted = DelayStartupToggle.IsOn;

            // the delay lives in the task trigger, so the task is written again
            if (WinAutostartService.Apply(true, wanted))
            {
                SettingsService.Instance.DelayStartup = wanted;
                return;
            }

            _isLoading = true;
            DelayStartupToggle.IsOn = !wanted;
            _isLoading = false;

            await ShowInfoDialog(AppStrings.Get("Settings_StartupFailedTitle"), AppStrings.Get("Settings_StartupFailedMessage"));
        }

        private void StartMinimizedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.StartMinimizedToTray = StartMinimizedToggle.IsOn;
        }

        private void CheckUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.CheckUpdatesOnStartup = CheckUpdatesToggle.IsOn;
        }

        // title bar status readout
        private void StatusReadoutToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // read once, the handler writes two settings
            bool isOn = StatusReadoutToggle.IsOn;
            _isWritingStatusReadout = true;

            // switching on also undoes a collapse from the title bar button, or the readout would stay hidden
            if (isOn) SettingsService.Instance.StatusReadoutCollapsed = false;

            SettingsService.Instance.StatusReadoutEnabled = isOn;
            _isWritingStatusReadout = false;

            UpdateStatusReadoutCardStates();
        }

        private void StatusLhmGroupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _isWritingStatusReadout = true;
            SettingsService.Instance.StatusLhmGroupEnabled = StatusLhmGroupToggle.IsOn;
            _isWritingStatusReadout = false;

            UpdateStatusReadoutCardStates();
        }

        private void StatusWindowsGroupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _isWritingStatusReadout = true;
            SettingsService.Instance.StatusWindowsGroupEnabled = StatusWindowsGroupToggle.IsOn;
            _isWritingStatusReadout = false;

            UpdateStatusReadoutCardStates();
        }

        // takes effect on the next launch; MainWindow reads it once at the splash reveal
        private void StartupPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (StartupPageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out StartupPage page))
            {
                SettingsService.Instance.StartupPage = page;
            }
        }

        private void StatusGroupOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (StatusGroupOrderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out StatusGroupOrder order))
            {
                _isWritingStatusReadout = true;
                SettingsService.Instance.StatusGroupOrder = order;
                _isWritingStatusReadout = false;
            }
        }

        private void RestoreStatusReadoutSelection()
        {
            StatusReadoutToggle.IsOn = SettingsService.Instance.StatusReadoutEnabled;
            StatusLhmGroupToggle.IsOn = SettingsService.Instance.StatusLhmGroupEnabled;
            StatusWindowsGroupToggle.IsOn = SettingsService.Instance.StatusWindowsGroupEnabled;

            string currentOrder = SettingsService.Instance.StatusGroupOrder.ToString();
            foreach (ComboBoxItem item in StatusGroupOrderComboBox.Items)
            {
                if (item.Tag?.ToString() == currentOrder)
                {
                    StatusGroupOrderComboBox.SelectedItem = item;
                    break;
                }
            }

            UpdateStatusReadoutCardStates();
        }

        // the rows below the master toggle need it on, and an order needs both readouts shown
        private void UpdateStatusReadoutCardStates()
        {
            bool readoutEnabled = StatusReadoutToggle.IsOn;

            StatusLhmGroupCard.IsEnabled = readoutEnabled;
            StatusWindowsGroupCard.IsEnabled = readoutEnabled;
            StatusGroupOrderCard.IsEnabled = readoutEnabled && StatusLhmGroupToggle.IsOn && StatusWindowsGroupToggle.IsOn;
        }

        // csv format; read only when a recording starts, so a switch never touches an open file
        private void CsvNumberFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (CsvNumberFormatComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out CsvNumberFormat format))
            {
                SettingsService.Instance.CsvNumberFormat = format;
            }

            UpdateCsvFormatExample();
        }

        private void CsvDecimalPlacesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (CsvDecimalPlacesComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && int.TryParse(tag, out int places))
            {
                SettingsService.Instance.CsvDecimalPlaces = places;
            }

            UpdateCsvFormatExample();
        }

        private void CsvIncludeUnitsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsService.Instance.CsvIncludeUnits = CsvIncludeUnitsToggle.IsOn;

            UpdateCsvFormatExample();
        }

        private void CsvPauseSeamComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (CsvPauseSeamComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out CsvPauseSeam seam))
            {
                SettingsService.Instance.CsvPauseSeam = seam;
            }
        }

        private void RestoreCsvFormatSelection()
        {
            var settings = SettingsService.Instance;

            SelectByTag(CsvNumberFormatComboBox, settings.CsvNumberFormat.ToString());
            SelectByTag(CsvDecimalPlacesComboBox, settings.CsvDecimalPlaces.ToString());
            SelectByTag(CsvPauseSeamComboBox, settings.CsvPauseSeam.ToString());

            CsvIncludeUnitsToggle.IsOn = settings.CsvIncludeUnits;

            UpdateCsvFormatExample();
        }

        // one sample row for all four options, built through the CsvRowFormat and SensorUnitFormatter a recording uses,
        // so it never drifts from the file
        private void UpdateCsvFormatExample()
        {
            var format = CsvRowFormat.Resolve();

            string clock = format.FormatValue(2515.862, SensorUnitFormatter.GetRawUnit("Clock"));
            string temperature = format.FormatValue(41.375, SensorUnitFormatter.GetRawUnit("Temperature"));

            CsvFormatExampleTextBlock.Text = clock + format.Separator + temperature;
        }

        // the entry whose Tag matches, for every combo box restored from a single value
        private static void SelectByTag(ComboBox comboBox, string tag)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        // graph line style, stepline or smooth; one global switch for every graph
        private void GraphLineStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (GraphLineStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out GraphLineStyle style))
            {
                SettingsService.Instance.GraphLineStyle = style;
            }
        }

        private void RestoreGraphLineStyleSelection()
        {
            string current = SettingsService.Instance.GraphLineStyle.ToString();

            foreach (ComboBoxItem item in GraphLineStyleComboBox.Items)
            {
                if (item.Tag?.ToString() == current)
                {
                    GraphLineStyleComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // area fill fade, the second global graph switch
        private void GraphFillFadeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsService.Instance.GraphFillFade = GraphFillFadeToggle.IsOn;
        }

        private void RestoreGraphFillFadeSelection()
        {
            GraphFillFadeToggle.IsOn = SettingsService.Instance.GraphFillFade;
        }

        // bytes or bits, separately for sizes and speeds, so a network speed reads Mbit/s while memory stays in MB
        private void DataSizeUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (DataSizeUnitComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out DataUnitBasis basis))
            {
                SettingsService.Instance.DataSizeUnitBasis = basis;
            }
        }

        private void DataSpeedUnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (DataSpeedUnitComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out DataUnitBasis basis))
            {
                SettingsService.Instance.DataSpeedUnitBasis = basis;
            }
        }

        private void RestoreDataUnitSelection()
        {
            SelectByTag(DataSizeUnitComboBox, SettingsService.Instance.DataSizeUnitBasis.ToString());
            SelectByTag(DataSpeedUnitComboBox, SettingsService.Instance.DataSpeedUnitBasis.ToString());
        }

        // every hardware category glyph: start page tiles, sensor list and hidden sensor group headers, hardware view
        // headers; (graph colors have their own source per surface)
        private void HardwareIconColorsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            SettingsService.Instance.UseHardwareIconColors = HardwareIconColorsToggle.IsOn;
        }

        private void RestoreHardwareIconColorsSelection()
        {
            HardwareIconColorsToggle.IsOn = SettingsService.Instance.UseHardwareIconColors;
        }


        // === performance page appearance settings ===

        // the overview range, plus one each for the dense cpu all-threads and gpu extended grids
        private void PerformanceGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.PerformanceGraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void PerformanceCpuExtendedGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.PerformanceCpuExtendedGraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void PerformanceGpuExtendedGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.PerformanceGpuExtendedGraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void RestorePerformanceGraphTimeSpanSelection()
        {
            SelectTimeSpanItem(PerformanceGraphTimeSpanComboBox, SettingsService.Instance.PerformanceGraphTimeSpanSeconds);
            SelectTimeSpanItem(PerformanceCpuExtendedGraphTimeSpanComboBox, SettingsService.Instance.PerformanceCpuExtendedGraphTimeSpanSeconds);
            SelectTimeSpanItem(PerformanceGpuExtendedGraphTimeSpanComboBox, SettingsService.Instance.PerformanceGpuExtendedGraphTimeSpanSeconds);
        }

        // nothing selected for a value the list does not have
        private static void SelectTimeSpanItem(ComboBox comboBox, double timeSpanSeconds)
        {
            comboBox.SelectedItem = GraphTimeRanges.Find(comboBox.ItemsSource as IReadOnlyList<GraphTimeRange>, timeSpanSeconds);
        }


        // === widget appearance settings ===

        // background material
        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.BackdropType = tag;
            }

            UpdateBackgroundMaterialCardStates();
        }

        // the backdrop decides which rows apply: the opacity sliders are acrylic only, a color source only exists for
        // acrylic and solid (mica brings its own); the picker follows its source
        private void UpdateBackgroundMaterialCardStates()
        {
            string backdrop = SettingsService.Instance.BackdropType;

            TintOpacityCard.IsEnabled = backdrop == "Acrylic";
            LuminosityOpacityCard.IsEnabled = backdrop == "Acrylic";
            BackgroundColorSourceCard.IsEnabled = backdrop is "Acrylic" or "None";
            WidgetBackgroundColorPicker.IsEnabled = !SettingsService.Instance.UseAccentColor;
        }

        private void BackgroundColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.UseAccentColor = (tag == "Accent");
            }

            UpdateBackgroundMaterialCardStates();
        }

        private void TintSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SettingsService.Instance.TintOpacity = (float)e.NewValue;
        }

        private void LuminositySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SettingsService.Instance.LuminosityOpacity = (float)e.NewValue;
        }

        private void WidgetBackgroundColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (_isLoading) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                // a picked color switches the source to custom
                SettingsService.Instance.UseAccentColor = false;
                BackgroundColorSourceComboBox.SelectedIndex = 1;

                SettingsService.Instance.CustomTintColor = colorPicker.SelectedColor;
                UpdateBackgroundMaterialCardStates();
            }
        }

        private void RestoreBackgroundMaterialSettings()
        {
            BackgroundColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseAccentColor ? 0 : 1;

            string currentBackdrop = SettingsService.Instance.BackdropType;
            foreach (ComboBoxItem item in BackdropComboBox.Items)
            {
                if (item.Tag?.ToString() == currentBackdrop)
                {
                    BackdropComboBox.SelectedItem = item;
                    break;
                }
            }

            TintSlider.Value = SettingsService.Instance.TintOpacity;
            LuminositySlider.Value = SettingsService.Instance.LuminosityOpacity;
            WidgetBackgroundColorPicker.SelectedColor = SettingsService.Instance.CustomTintColor;

            UpdateBackgroundMaterialCardStates();
        }

        // graph
        private void GraphColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GraphColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out GraphColorSource source))
            {
                SettingsService.Instance.GraphColorSource = source;
            }

            UpdateGraphColorPickerStates();
        }

        // a picker only while its selector says custom
        private void UpdateGraphColorPickerStates()
        {
            GraphColorPicker.IsEnabled = SettingsService.Instance.GraphColorSource == GraphColorSource.Custom;
            TaskbarGraphColorPicker.IsEnabled = SettingsService.Instance.TaskbarGraphColorSource == GraphColorSource.Custom;
        }
        private void GraphColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (_isLoading) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                // a picked color switches the source to custom
                SettingsService.Instance.GraphColorSource = GraphColorSource.Custom;
                SelectByTag(GraphColorSourceComboBox, nameof(GraphColorSource.Custom));

                SettingsService.Instance.GraphCustomColor = colorPicker.SelectedColor;
                UpdateGraphColorPickerStates();
            }
        }

        private void GraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.GraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void RestoreGraphColorSettings()
        {
            SelectByTag(GraphColorSourceComboBox, SettingsService.Instance.GraphColorSource.ToString());
            GraphColorPicker.SelectedColor = SettingsService.Instance.GraphCustomColor;

            UpdateGraphColorPickerStates();
        }

        private void RestoreGraphTimeSpanSelection()
        {
            SelectTimeSpanItem(GraphTimeSpanComboBox, SettingsService.Instance.GraphTimeSpanSeconds);
        }


        // === taskbar & flyout appearance settings ===

        // background material
        private void TaskbarBackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskbarBackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.TaskbarBackdropType = tag;
            }

            UpdateTaskbarBackgroundMaterialCardStates();
        }

        // same rule as the widget window, see UpdateBackgroundMaterialCardStates
        private void UpdateTaskbarBackgroundMaterialCardStates()
        {
            string backdrop = SettingsService.Instance.TaskbarBackdropType;

            TaskbarTintOpacityCard.IsEnabled = backdrop == "Acrylic";
            TaskbarLuminosityOpacityCard.IsEnabled = backdrop == "Acrylic";
            TaskbarBackgroundColorSourceCard.IsEnabled = backdrop is "Acrylic" or "None";
            TaskbarBackgroundColorPicker.IsEnabled = !SettingsService.Instance.TaskbarUseAccentColor;
        }

        private void TaskbarBackgroundColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskbarBackgroundColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.TaskbarUseAccentColor = (tag == "Accent");
            }

            UpdateTaskbarBackgroundMaterialCardStates();
        }

        private void TaskbarTintSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SettingsService.Instance.TaskbarTintOpacity = (float)e.NewValue;
        }

        private void TaskbarLuminositySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SettingsService.Instance.TaskbarLuminosityOpacity = (float)e.NewValue;
        }

        private void TaskbarBackgroundColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (_isLoading) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                SettingsService.Instance.TaskbarUseAccentColor = false;
                TaskbarBackgroundColorSourceComboBox.SelectedIndex = 1;
                SettingsService.Instance.TaskbarCustomTintColor = colorPicker.SelectedColor;
                UpdateTaskbarBackgroundMaterialCardStates();
            }
        }

        private void RestoreTaskbarBackgroundMaterialSettings()
        {
            TaskbarBackgroundColorSourceComboBox.SelectedIndex = SettingsService.Instance.TaskbarUseAccentColor ? 0 : 1;

            string currentBackdrop = SettingsService.Instance.TaskbarBackdropType;
            foreach (ComboBoxItem item in TaskbarBackdropComboBox.Items)
            {
                if (item.Tag?.ToString() == currentBackdrop)
                {
                    TaskbarBackdropComboBox.SelectedItem = item;
                    break;
                }
            }

            TaskbarTintSlider.Value = SettingsService.Instance.TaskbarTintOpacity;
            TaskbarLuminositySlider.Value = SettingsService.Instance.TaskbarLuminosityOpacity;
            TaskbarBackgroundColorPicker.SelectedColor = SettingsService.Instance.TaskbarCustomTintColor;

            UpdateTaskbarBackgroundMaterialCardStates();
        }

        // graph
        private void TaskbarGraphColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskbarGraphColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out GraphColorSource source))
            {
                SettingsService.Instance.TaskbarGraphColorSource = source;
            }

            UpdateGraphColorPickerStates();
        }

        private void TaskbarGraphColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (_isLoading) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                SettingsService.Instance.TaskbarGraphColorSource = GraphColorSource.Custom;
                SelectByTag(TaskbarGraphColorSourceComboBox, nameof(GraphColorSource.Custom));
                SettingsService.Instance.TaskbarGraphCustomColor = colorPicker.SelectedColor;
                UpdateGraphColorPickerStates();
            }
        }

        private void TaskbarGraphBackgroundSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskbarGraphBackgroundSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.TaskbarUseTransparentGraphBackground = (tag == "Transparent");
            }
        }

        private void TaskbarGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.TaskbarGraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void RestoreTaskbarGraphColorSettings()
        {
            SelectByTag(TaskbarGraphColorSourceComboBox, SettingsService.Instance.TaskbarGraphColorSource.ToString());
            TaskbarGraphColorPicker.SelectedColor = SettingsService.Instance.TaskbarGraphCustomColor;

            UpdateGraphColorPickerStates();
        }

        private void RestoreTaskbarGraphBackgroundSourceSelection()
        {
            TaskbarGraphBackgroundSourceComboBox.SelectedIndex =
                SettingsService.Instance.TaskbarUseTransparentGraphBackground ? 1 : 0;
        }

        private void RestoreTaskbarGraphTimeSpanSelection()
        {
            SelectTimeSpanItem(TaskbarGraphTimeSpanComboBox, SettingsService.Instance.TaskbarGraphTimeSpanSeconds);
        }

        private void TaskbarGraphWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.TaskbarGraphWidthDip = (int)e.NewValue;
        }

        private void RestoreTaskbarGraphWidthSelection()
        {
            TaskbarGraphWidthSlider.Value = SettingsService.Instance.TaskbarGraphWidthDip;
        }

        // slot layout on a side taskbar
        private void TaskbarSideGraphDirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (TaskbarSideGraphDirectionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.TaskbarSideGraphDirection = tag;
            }
        }

        private void TaskbarSideTitleLinesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (TaskbarSideTitleLinesComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int lines))
            {
                SettingsService.Instance.TaskbarSideTitleLines = lines;
            }
        }

        private void RestoreTaskbarSideSlotSelection()
        {
            SelectByTag(TaskbarSideGraphDirectionComboBox, SettingsService.Instance.TaskbarSideGraphDirection);
            SelectByTag(TaskbarSideTitleLinesComboBox, SettingsService.Instance.TaskbarSideTitleLines.ToString());
        }

        // flyout alignment over the widget
        private void TaskbarFlyoutAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (TaskbarFlyoutAlignmentComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.TaskbarFlyoutAlignment = tag;
            }
        }

        private void RestoreTaskbarFlyoutAlignmentSelection()
        {
            string currentAlignment = SettingsService.Instance.TaskbarFlyoutAlignment;

            foreach (ComboBoxItem item in TaskbarFlyoutAlignmentComboBox.Items)
            {
                if (item.Tag?.ToString() == currentAlignment)
                {
                    TaskbarFlyoutAlignmentComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // flyout graphs; one time range and one slot height for every edge
        private void TaskbarFlyoutGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is GraphTimeRange option)
            {
                SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds = option.Seconds;
            }
        }

        private void TaskbarFlyoutGraphHeightSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.TaskbarFlyoutGraphHeightDip = e.NewValue;
        }

        private void RestoreTaskbarFlyoutGraphSelection()
        {
            SelectTimeSpanItem(TaskbarFlyoutGraphTimeSpanComboBox, SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds);
            TaskbarFlyoutGraphHeightSlider.Value = SettingsService.Instance.TaskbarFlyoutGraphHeightDip;
        }

        // flyout shortcut; the dialog records it, the registered one is suspended meanwhile or it would swallow the keys
        private async void FlyoutShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FlyoutShortcutDialog(SettingsService.Instance.TaskbarFlyoutShortcut)
            {
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };

            FlyoutShortcutRegistration.SetSuspended(true);
            try
            {
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    SettingsService.Instance.TaskbarFlyoutShortcut = dialog.Shortcut;
                    ApplyFlyoutShortcutButton();
                }
            }
            finally
            {
                FlyoutShortcutRegistration.SetSuspended(false);
            }
        }

        // the keys or "None"; the name says the same for a screen reader
        private void ApplyFlyoutShortcutButton()
        {
            var shortcut = SettingsService.Instance.TaskbarFlyoutShortcut;
            var keys = shortcut != null ? WinHotkeyService.FormatKeys(shortcut.Modifiers, shortcut.VirtualKey) : new List<string>();

            FlyoutShortcutDialog.FillKeys(FlyoutShortcutKeysPanel, keys, (Style)FlyoutShortcutKeysPanel.Resources["ShortcutKeyStyle"]);
            FlyoutShortcutNoneText.Visibility = shortcut == null ? Visibility.Visible : Visibility.Collapsed;

            string text = shortcut != null ? string.Join("+", keys) : AppStrings.Get("Settings_FlyoutShortcutNone");
            AutomationProperties.SetName(FlyoutShortcutButton, AppStrings.Format("Settings_FlyoutShortcutName", text));
        }

        // widget drag lock
        private void LockWidgetPositionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.TaskbarWidgetPositionLocked = LockWidgetPositionToggle.IsOn;
        }

        private void RestoreLockWidgetPositionSelection()
        {
            LockWidgetPositionToggle.IsOn = SettingsService.Instance.TaskbarWidgetPositionLocked;
        }


        // === backup and restore settings ===

        // the real path, since the folder differs per channel and the store one is hard to guess; (the markup
        // description is design time only)
        private void ShowAppDataFolderPath()
        {
            string folder = PersistenceService.Instance.RootFolder;
            if (!string.IsNullOrEmpty(folder)) AppDataFolderCard.Description = folder;
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


        // export and import
        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(FluentSensors.MainWindow.CurrentInstance);
            string suggestedName = $"FluentSensors-Backup-{DateTime.Now:yyyy-MM-dd}.zip";

            string path = Win32FileDialogHelper.PickSaveFile(hwnd, AppStrings.Get("Settings_ExportPickerTitle"), suggestedName, AppStrings.Get("Settings_BackupFileType"), "zip");
            if (path == null) return; // user cancelled

            try
            {
                // settings.json with the live state, even if nothing was saved this session
                SettingsService.Instance.SaveImmediate();
                PersistenceService.Instance.ExportBackup(path);
                await ShowInfoDialog(AppStrings.Get("Settings_ExportSuccessTitle"), AppStrings.Get("Settings_ExportSuccessMessage"));
            }
            catch
            {
                await ShowInfoDialog(AppStrings.Get("Settings_ExportFailedTitle"), AppStrings.Get("Settings_ExportFailedMessage"));
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(FluentSensors.MainWindow.CurrentInstance);

            string path = Win32FileDialogHelper.PickOpenFile(hwnd, AppStrings.Get("Settings_ImportPickerTitle"), AppStrings.Get("Settings_BackupFileType"), "zip");
            if (path == null) return; // user cancelled

            bool confirmed = await ConfirmAction(
                AppStrings.Get("Settings_ImportConfirmTitle"),
                AppStrings.Get("Settings_ImportConfirmMessage"),
                AppStrings.Get("Settings_ImportConfirm"));
            if (!confirmed) return;

            bool success = PersistenceService.Instance.ImportBackup(path);
            if (success)
            {
                // every singleton reloads from the imported files right away, so nothing saving on the way out writes
                // pre-import data (beyond the MainWindow.AppWindow_Changed guard)
                SettingsService.Instance.LoadFromData(PersistenceService.Instance.LoadSettings());
                WindowStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadWindowStates());
                SensorStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorStates());
                SensorSwitchStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSwitchStates());

                RestartApp();
            }
            else
            {
                await ShowInfoDialog(AppStrings.Get("Settings_ImportFailedTitle"), AppStrings.Get("Settings_ImportFailedMessage"));
            }
        }

        private async Task ShowInfoDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = AppStrings.Get("Common_OK"),
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };
            await dialog.ShowAsync();
        }

        // reset
        private async void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset(AppStrings.Get("Settings_ResetWhatAll")))
            {
                PersistenceService.Instance.ResetAll();
                RestartApp();
            }
        }

        private async void ResetGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset(AppStrings.Get("Settings_ResetWhatGeneral")))
            {
                PersistenceService.Instance.ResetSettings();
                RestartApp();
            }
        }

        // window and page states: window position and size, the title bar status readout, the sensor
        // picked per performance page slot
        private async void ResetWindowAndPageStates_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset(AppStrings.Get("Settings_ResetWhatWindowStates")))
            {
                PersistenceService.Instance.ResetWindowStates();
                PersistenceService.Instance.ResetSensorSwitchStates();

                // these live in settings.json next to unrelated settings, so they reset through their setters;
                // ForceExit() flushes the queued save
                var defaultSettings = new AppSettingsData();
                SettingsService.Instance.StatusReadoutEnabled = defaultSettings.StatusReadoutEnabled;
                SettingsService.Instance.StatusReadoutCollapsed = defaultSettings.StatusReadoutCollapsed;
                SettingsService.Instance.StatusLhmGroupEnabled = defaultSettings.StatusLhmGroupEnabled;
                SettingsService.Instance.StatusWindowsGroupEnabled = defaultSettings.StatusWindowsGroupEnabled;
                SettingsService.Instance.StatusGroupOrder = defaultSettings.StatusGroupOrder;

                RestartApp();
            }
        }

        private async void ResetSensorStates_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset(AppStrings.Get("Settings_ResetWhatSensorStates")))
            {
                PersistenceService.Instance.ResetSensorStates();
                PersistenceService.Instance.ResetSensorSelections();
                RestartApp();
            }
        }

        private Task<bool> ConfirmReset(string what)
        {
            return ConfirmAction(AppStrings.Format("Settings_ResetConfirmTitle", what), AppStrings.Get("Settings_ResetConfirmMessage"));
        }

        private async Task<bool> ConfirmAction(string title, string message, string? confirmText = null)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText ?? AppStrings.Get("Settings_ResetConfirm"),
                CloseButtonText = AppStrings.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private void RestartApp()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true
            });

            FluentSensors.MainWindow.CurrentInstance?.ForceExit();
        }
    }
}
