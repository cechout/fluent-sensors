using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

using FluentSensors.Persistence.Services;
using FluentSensors.Persistence.Models;
using FluentSensors.Core;
using FluentSensors.Core.Startup;
using FluentSensors.Common.Csv;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Features.Settings
{
    public sealed partial class SettingsPage : Page
    {
        // flag to prevent event handlers from firing during initialization
        private bool _isLoading = true;

        // set while this page writes a status readout setting itself, see OnStatusReadoutChanged
        private bool _isWritingStatusReadout;


        // === constructor ===

        public SettingsPage()
        {
            this.InitializeComponent();

            // restore the previous user selections
            RestoreThemeSelection();
            RestoreIntervalSelection();
            RestoreMinimizeToTraySelection();
            RestoreStartupSelection();
            RestoreStatusReadoutSelection();
            RestoreCsvFormatSelection();
            RestoreGraphLineStyleSelection();
            RestoreGraphFillFadeSelection();
            RestoreHardwareIconColorsSelection();

            RestorePerformanceGraphTimeSpanSelection();

            RestoreBackgroundMaterialSettings();
            RestoreGraphColorSettings();
            RestoreGraphTimeSpanSelection();

            RestoreTaskbarBackgroundMaterialSettings();
            RestoreTaskbarGraphColorSettings();
            RestoreTaskbarGraphBackgroundSourceSelection();
            RestoreTaskbarGraphTimeSpanSelection();
            RestoreTaskbarGraphWidthSelection();
            RestoreTaskbarFlyoutAlignmentSelection();
            RestoreLockWidgetPositionSelection();

            ShowAppDataFolderPath();


            // event listeners
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

        // the toggle button in the title bar writes the same master setting these controls show, so they can go
        // stale while this page sits in the navigation cache
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.StatusReadoutChanged += OnStatusReadoutChanged;
            OnStatusReadoutChanged();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.StatusReadoutChanged -= OnStatusReadoutChanged;
        }

        // only meant for writes from outside this page; a write from here echoes straight back into this method,
        // and restoring mid handler would push the control the user is operating back to a half written state
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

                // we get the absolute root element of the current window
                if (this.XamlRoot?.Content is FrameworkElement rootElement)
                {
                    // Match-Mapping for the ElementTheme enum
                    rootElement.RequestedTheme = themeTag switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default // system default
                    };
                }
            }
        }

        private void RestoreThemeSelection()
        {
            // we read the current theme value from the SettingsService
            string currentTheme = SettingsService.Instance.AppTheme;

            // we search through all the items in the ThemeComboBox and compare their Tag with the current theme
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    // match found -> activate the item
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // update interval
        private void IntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int newIntervalMs))
                {
                    // we access the one HardwareMonitorService instance and change the interval at runtime
                    HardwareMonitorService.Instance.UpdateIntervalMs = newIntervalMs;
                    SettingsService.Instance.SaveDebounced();
                }
            }
        }

        private void RestoreIntervalSelection()
        {
            // we read the current interval value from the HardwareMonitorService instance
            int currentInterval = HardwareMonitorService.Instance.UpdateIntervalMs;

            // we search through all the items in the IntervalComboBox and compare their tag with the current interval value
            foreach (ComboBoxItem item in IntervalComboBox.Items)
            {
                if (item.Tag?.ToString() == currentInterval.ToString())
                {
                    // match found -> activate the item
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

        // the scheduled task is the authority on autostart, not the setting, so a task someone removed by hand in
        // the task scheduler shows up here as off rather than as a switch that lies
        // a portable build has no autostart at all, because the task would outlive the folder it points at
        private void RestoreStartupSelection()
        {
            var settings = SettingsService.Instance;

            StartMinimizedToggle.IsOn = settings.StartMinimizedToTray;
            CheckUpdatesToggle.IsOn = settings.CheckUpdatesOnStartup;

            // deliberately above the two early returns below, the landing page is not tied to autostart at all
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
                RunOnStartupCard.Description = "Not available in the portable version, it would leave a scheduled task behind";
                RunOnStartupToggle.Visibility = Visibility.Collapsed;
                DelayStartupCard.Visibility = Visibility.Collapsed;
                UpdateStartupCardStates();
                return;
            }

            // a task pointing at a moved exe is repaired at app start, not here, so it is fixed even for someone
            // who never opens this page
            bool taskExists = WinAutostartService.IsEnabled();
            if (settings.RunOnStartup != taskExists) settings.RunOnStartup = taskExists;

            RunOnStartupToggle.IsOn = taskExists;
            DelayStartupToggle.IsOn = settings.DelayStartup;
            UpdateStartupCardStates();
        }

        // both rows only do anything while windows is the one launching the app: a delay needs a scheduled task
        // to delay, and start-minimized is explicitly about the sign-in launch, see MainWindow.StartsHiddenInTray
        // the portable build has no task at all, so neither row can be honoured there whatever the toggle says
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

            // the task scheduler refused, so put the switch back rather than showing a state that does not exist
            _isLoading = true;
            RunOnStartupToggle.IsOn = !wanted;
            _isLoading = false;
            UpdateStartupCardStates();

            await ShowInfoDialog("Startup", "Windows did not accept the change to the scheduled task.");
        }

        private async void DelayStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            bool wanted = DelayStartupToggle.IsOn;

            // the delay lives in the task trigger, so changing it means writing the task again
            if (WinAutostartService.Apply(true, wanted))
            {
                SettingsService.Instance.DelayStartup = wanted;
                return;
            }

            _isLoading = true;
            DelayStartupToggle.IsOn = !wanted;
            _isLoading = false;

            await ShowInfoDialog("Startup", "Windows did not accept the change to the scheduled task.");
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

            // read once up front: this handler writes two settings, and the control must not be able to change
            // underneath the second write
            bool isOn = StatusReadoutToggle.IsOn;
            _isWritingStatusReadout = true;

            // switching it back on here also undoes a collapse from the title bar button, otherwise the readout would
            // stay hidden while this toggle claims it is on
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

        // takes effect on the next launch, MainWindow reads the setting once during the splash reveal
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

        // the rows below the master toggle only do anything while it is on, and an order only exists while both
        // readouts are actually shown
        private void UpdateStatusReadoutCardStates()
        {
            bool readoutEnabled = StatusReadoutToggle.IsOn;

            StatusLhmGroupCard.IsEnabled = readoutEnabled;
            StatusWindowsGroupCard.IsEnabled = readoutEnabled;
            StatusGroupOrderCard.IsEnabled = readoutEnabled && StatusLhmGroupToggle.IsOn && StatusWindowsGroupToggle.IsOn;
        }

        // csv format; all four pieces are only read when a recording starts, so switching any of them never
        // touches an open file
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

        // one sample row that stands for all four options at once
        //
        // built through the same CsvRowFormat a recording uses, so what the expander shows and what lands in the
        // file can not drift apart; the units come from SensorUnitFormatter for the same reason
        private void UpdateCsvFormatExample()
        {
            var format = CsvRowFormat.Resolve();

            string clock = format.FormatValue(2515.862, SensorUnitFormatter.GetUnit("Clock"));
            string temperature = format.FormatValue(41.375, SensorUnitFormatter.GetUnit("Temperature"));

            CsvFormatExampleTextBlock.Text = clock + format.Separator + temperature;
        }

        // picks the entry whose Tag matches, used by every combo box that is restored from a single value
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

        // graph line style (stepline / smooth), one global switch for every graph
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

        // reaches every hardware category glyph: start page tiles, sensor list and hidden sensor group headers,
        // and the hardware views own headers
        //
        // graph colors are deliberately not here; each surface picks its own source next to its custom color, see
        // GraphColorSourceComboBox and TaskbarGraphColorSourceComboBox
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

        // two ranges because the page has two graph densities; Extended covers the cpu all-threads and
        // gpu extended grids, which show many small graphs at once
        private void PerformanceGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && double.TryParse(selectedItem.Tag.ToString(), out double newTimeSpanSeconds))
                {
                    SettingsService.Instance.PerformanceGraphTimeSpanSeconds = newTimeSpanSeconds;
                }
            }
        }

        private void PerformanceExtendedGraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && double.TryParse(selectedItem.Tag.ToString(), out double newTimeSpanSeconds))
                {
                    SettingsService.Instance.PerformanceExtendedGraphTimeSpanSeconds = newTimeSpanSeconds;
                }
            }
        }

        private void RestorePerformanceGraphTimeSpanSelection()
        {
            SelectTimeSpanItem(PerformanceGraphTimeSpanComboBox, SettingsService.Instance.PerformanceGraphTimeSpanSeconds);
            SelectTimeSpanItem(PerformanceExtendedGraphTimeSpanComboBox, SettingsService.Instance.PerformanceExtendedGraphTimeSpanSeconds);
        }

        private static void SelectTimeSpanItem(ComboBox comboBox, double timeSpanSeconds)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag?.ToString() == timeSpanSeconds.ToString())
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
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

        // the backdrop in the header decides which rows below it do anything: both opacity sliders are acrylic
        // only, and mica brings its own color, so only acrylic and solid have a color to source at all
        // the picker answers to the source next to it rather than to the backdrop
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
                // if user manually picks a color, we switch the source to "custom"
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

        // Graph
        private void GraphColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GraphColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out GraphColorSource source))
            {
                SettingsService.Instance.GraphColorSource = source;
            }

            UpdateGraphColorPickerStates();
        }

        // a color picker is only worth reaching while the selector next to it actually says custom
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
                // if user picks a color for the graph, we switch the source to "custom"
                SettingsService.Instance.GraphColorSource = GraphColorSource.Custom;
                SelectByTag(GraphColorSourceComboBox, nameof(GraphColorSource.Custom));

                SettingsService.Instance.GraphCustomColor = colorPicker.SelectedColor;
                UpdateGraphColorPickerStates();
            }
        }

        private void GraphTimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && double.TryParse(selectedItem.Tag.ToString(), out double newTimeSpanSeconds))
                {
                    SettingsService.Instance.GraphTimeSpanSeconds = newTimeSpanSeconds;
                }
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
            double currentTimeSpanSeconds = SettingsService.Instance.GraphTimeSpanSeconds;

            foreach (ComboBoxItem item in GraphTimeSpanComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTimeSpanSeconds.ToString())
                {
                    GraphTimeSpanComboBox.SelectedItem = item;
                    break;
                }
            }
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

        // Graph
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

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && double.TryParse(selectedItem.Tag.ToString(), out double newTimeSpanSeconds))
                {
                    SettingsService.Instance.TaskbarGraphTimeSpanSeconds = newTimeSpanSeconds;
                }
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
            double currentTimeSpanSeconds = SettingsService.Instance.TaskbarGraphTimeSpanSeconds;

            foreach (ComboBoxItem item in TaskbarGraphTimeSpanComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTimeSpanSeconds.ToString())
                {
                    TaskbarGraphTimeSpanComboBox.SelectedItem = item;
                    break;
                }
            }
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

        // the folder differs per channel and a store build puts it somewhere nobody would guess, so the card names
        // the real path instead of describing it in the abstract; the description in the markup is only what shows
        // at design time
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

            string path = Win32FileDialogHelper.PickSaveFile(hwnd, "Export Settings", suggestedName, "Backup File", "zip");
            if (path == null) return; // user cancelled

            try
            {
                // ensure settings.json reflects the live state even if it was never re-written to disk this session
                SettingsService.Instance.SaveImmediate();
                PersistenceService.Instance.ExportBackup(path);
                await ShowInfoDialog("Export Successful", "Your settings have been exported.");
            }
            catch
            {
                await ShowInfoDialog("Export Failed", "The settings could not be exported.");
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(FluentSensors.MainWindow.CurrentInstance);

            string path = Win32FileDialogHelper.PickOpenFile(hwnd, "Import Settings", "Backup File", "zip");
            if (path == null) return; // user cancelled

            bool confirmed = await ConfirmAction(
                "Import Settings?",
                "This will overwrite all current settings, window states, sensor states, and sensor switch choices, then restart the app.",
                "Import");
            if (!confirmed) return;

            bool success = PersistenceService.Instance.ImportBackup(path);
            if (success)
            {
                // reload every in-memory singleton from the freshly imported files immediately; otherwise, even with the
                // AppWindow_Changed guard above, any other future code path that saves during shutdown would still be working
                // with stale pre-import data
                SettingsService.Instance.LoadFromData(PersistenceService.Instance.LoadSettings());
                WindowStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadWindowStates());
                SensorStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorStates());
                SensorSwitchStateService.Instance.LoadFromDisk(PersistenceService.Instance.LoadSensorSwitchStates());

                RestartApp();
            }
            else
            {
                await ShowInfoDialog("Import Failed", "The selected file is not a valid FluentSensors backup.");
            }
        }

        private async Task ShowInfoDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
                RequestedTheme = DialogTheme.For(this.XamlRoot)
            };
            await dialog.ShowAsync();
        }

        // reset
        private async void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset("All Settings"))
            {
                PersistenceService.Instance.ResetAll();
                RestartApp();
            }
        }

        private async void ResetGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset("General Settings"))
            {
                PersistenceService.Instance.ResetSettings();
                RestartApp();
            }
        }

        // window and page states cover three things that all fall under "what the window/page layout currently
        // looks like": window position/size, the title bar status readout, and which sensor is picked per graph
        // slot on the Performance page
        private async void ResetWindowAndPageStates_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset("Window and Page States"))
            {
                PersistenceService.Instance.ResetWindowStates();
                PersistenceService.Instance.ResetSensorSwitchStates();

                // these live inside settings.json next to unrelated general settings (theme, tray behavior, etc), so
                // they are reset in place through their own setters instead of deleting that whole file; the debounced
                // save this queues still reaches disk before restart, ForceExit() flushes any pending write on its way out
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
            if (await ConfirmReset("Sensor States"))
            {
                PersistenceService.Instance.ResetSensorStates();
                PersistenceService.Instance.ResetSensorSelections();
                RestartApp();
            }
        }

        private Task<bool> ConfirmReset(string what)
        {
            return ConfirmAction($"Reset {what}?", "This will restore the default values and restart the app. This action cannot be undone.");
        }

        private async Task<bool> ConfirmAction(string title, string message, string confirmText = "Reset")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText,
                CloseButtonText = "Cancel",
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
