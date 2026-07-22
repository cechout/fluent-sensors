using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

using FluentSensors.Persistence.Services;
using FluentSensors.Core;


namespace FluentSensors.Features.Settings
{
    public sealed partial class SettingsPage : Page
    {
        // flag to prevent event handlers from firing during initialization
        private bool _isLoading = true;


        // === constructor ===

        public SettingsPage()
        {
            this.InitializeComponent();

            // restore the previous user selections
            RestoreThemeSelection();
            RestoreIntervalSelection();
            RestoreMinimizeToTraySelection();

            RestoreBackgroundMaterialSettings();
            RestoreGraphColorSettings();
            RestoreGraphDataPointsSelection();


            // event listeners
            WidgetBackgroundColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                WidgetBackgroundColorPicker_SelectedColorChanged);

            GraphColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                GraphColorPicker_SelectedColorChanged);

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


        // === widget appearance settings ===
        
        // background material
        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.BackdropType = tag;
            }
        }

        private void BackgroundColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.UseAccentColor = (tag == "Accent");
            }
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
        }

        // Graph
        private void GraphColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GraphColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.UseGraphAccentColor = (tag == "Accent");
            }
        }
        private void GraphColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (_isLoading) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                // if user picks a color for the graph, we switch the source to "custom"
                SettingsService.Instance.UseGraphAccentColor = false;
                GraphColorSourceComboBox.SelectedIndex = 1;

                SettingsService.Instance.GraphCustomColor = colorPicker.SelectedColor;
            }
        }

        private void GraphDataPointsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int newDataPoints))
                {
                    SettingsService.Instance.GraphDataPoints = newDataPoints;
                }
            }
        }

        private void RestoreGraphColorSettings()
        {
            GraphColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseGraphAccentColor ? 0 : 1;
            GraphColorPicker.SelectedColor = SettingsService.Instance.GraphCustomColor;
        }

        private void RestoreGraphDataPointsSelection()
        {
            int currentDataPoints = SettingsService.Instance.GraphDataPoints;

            foreach (ComboBoxItem item in GraphDataPointsComboBox.Items)
            {
                if (item.Tag?.ToString() == currentDataPoints.ToString())
                {
                    GraphDataPointsComboBox.SelectedItem = item;
                    break;
                }
            }
        }


        // === backup and restore settings ===

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
                "This will overwrite all current settings, window states, and sensor states, then restart the app.",
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
                XamlRoot = this.XamlRoot
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

        private async void ResetWindowStates_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset("Window States"))
            {
                PersistenceService.Instance.ResetWindowStates();
                RestartApp();
            }
        }

        private async void ResetSensorStates_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmReset("Sensor States"))
            {
                PersistenceService.Instance.ResetSensorStates();
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
                XamlRoot = this.XamlRoot
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