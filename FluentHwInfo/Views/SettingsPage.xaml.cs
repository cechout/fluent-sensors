using FluentHwInfo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FluentHwInfo.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();

            // every time the page is created, call this new method to restore the last selected values in the combo boxes
            RestoreIntervalSelection();
            RestoreWidgetSettings();
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
        private void RestoreWidgetSettings()
        {
            ColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseAccentColor ? 0 : 1;
            CustomColorCard.Visibility = SettingsService.Instance.UseAccentColor ? Visibility.Collapsed : Visibility.Visible;
            TintPicker.Color = SettingsService.Instance.CustomTintColor;

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

            // restore slider values
            TintSlider.Value = SettingsService.Instance.TintOpacity;
            LuminositySlider.Value = SettingsService.Instance.LuminosityOpacity;
        }

        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null)
                {
                    // push the new value into the service. the service fires the event
                    // the widget listens to the event and changes the background live
                    SettingsService.Instance.BackdropType = selectedItem.Tag.ToString();
                }
            }
        }

        private void TintSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // cast to (float) because the slider provides doubles by default,
            // but our Windows API expects floats
            SettingsService.Instance.TintOpacity = (float)e.NewValue;
        }

        private void LuminositySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SettingsService.Instance.LuminosityOpacity = (float)e.NewValue;
        }

        private void ColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                bool useAccent = (selectedItem.Tag?.ToString() == "Accent");

                // Show or hide the ColorPicker based on selection
                if (CustomColorCard != null)
                {
                    CustomColorCard.Visibility = useAccent ? Visibility.Collapsed : Visibility.Visible;
                }

                SettingsService.Instance.UseAccentColor = useAccent;
            }
        }

        private void TintPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            SettingsService.Instance.CustomTintColor = args.NewColor;
        }
    }
}