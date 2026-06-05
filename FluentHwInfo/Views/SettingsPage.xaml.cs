using FluentHwInfo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FluentHwInfo.Views
{
    public sealed partial class SettingsPage : Page
    {
        // this is our "Türsteher" to prevent infinite loops when synchronizing the two color pickers
        private bool _isSyncingColor = false;

        public SettingsPage()
        {
            this.InitializeComponent();

            // every time the page is created, call this new method to restore the last selected values in the combo boxes
            RestoreIntervalSelection();
            RestoreWidgetSettings();

            // we register the same event handler for both color pickers, so that we can react to changes in either of them with the
            // same logic
            SolidColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                WidgetColorPicker_SelectedColorChanged);

            AcrylicColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                WidgetColorPicker_SelectedColorChanged);
        }


        // theme combo box
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string themeTag = selectedItem.Tag?.ToString();

                // we get the absolute root element of the current window
                if (this.XamlRoot?.Content is FrameworkElement rootElement)
                {
                    // Match-Mapping for the ElementTheme enum
                    rootElement.RequestedTheme = themeTag switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default // System Default
                    };
                }
            }
        }


        // interval combo box
        private void IntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int newIntervalMs))
                {
                    // we access the one HardwareMonitorService instance and change the interval at runtime
                    HardwareMonitorService.Instance.UpdateIntervalMs = newIntervalMs;

                    //System.Diagnostics.Debug.WriteLine($"Polling-Intervall changed to: {newIntervalMs} ms");
                }
            }
        }
        private void RestoreIntervalSelection()
        {
            // we read the current interval value from the HardwareMonitorService instance
            int currentInterval = HardwareMonitorService.Instance.UpdateIntervalMs;

            // we search through all the items in the IntervalComboBox and compare their Tag with the current interval value
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


        // widget combo box
        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                // just save the selected backdrop type
                SettingsService.Instance.BackdropType = tag;
            }
        }
        private void ColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                // just save the selected color source
                SettingsService.Instance.UseAccentColor = (tag == "Accent");

                // visibility of the custom color card depends on whether the user selected "custom" or not
                AcrylicCustomColorCard.Visibility = (tag == "Custom") ? Visibility.Visible : Visibility.Collapsed;
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
        private void WidgetColorPicker_SelectedColorChanged(DependencyObject sender, DependencyProperty dp)
        {
            // if we are already syncing the color, it means this event was fired by ourselves when we updated the other picker
            if (_isSyncingColor) return;

            if (sender is CommunityToolkit.WinUI.Controls.ColorPickerButton colorPicker)
            {
                // activate doorman
                _isSyncingColor = true;

                // save colour
                SettingsService.Instance.CustomTintColor = colorPicker.SelectedColor;

                // update the other color picker to keep them in sync
                if (sender == SolidColorPicker) AcrylicColorPicker.SelectedColor = colorPicker.SelectedColor;
                if (sender == AcrylicColorPicker) SolidColorPicker.SelectedColor = colorPicker.SelectedColor;

                // deactivate doorman 
                _isSyncingColor = false;
            }
        }
        private void RestoreWidgetSettings()
        {
            ColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseAccentColor ? 0 : 1;
            AcrylicCustomColorCard.Visibility = SettingsService.Instance.UseAccentColor ? Visibility.Collapsed : Visibility.Visible;

            // restore combo box selection 
            string currentBackdrop = SettingsService.Instance.BackdropType;
            foreach (ComboBoxItem item in BackdropComboBox.Items)
            {
                if (item.Tag?.ToString() == currentBackdrop)
                {
                    BackdropComboBox.SelectedItem = item;
                    break;
                }
            }

            // restore slider values & color pickers
            TintSlider.Value = SettingsService.Instance.TintOpacity;
            LuminositySlider.Value = SettingsService.Instance.LuminosityOpacity;

            SolidColorPicker.SelectedColor = SettingsService.Instance.CustomTintColor;
            AcrylicColorPicker.SelectedColor = SettingsService.Instance.CustomTintColor;
        }
    }
}