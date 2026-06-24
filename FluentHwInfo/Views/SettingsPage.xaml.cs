using FluentHwInfo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FluentHwInfo.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoading = true;

        public SettingsPage()
        {
            this.InitializeComponent();

            // restore the previous user selections
            RestoreThemeSelection();
            RestoreIntervalSelection();
            RestoreWidgetSettings();
            RestoreGraphDataPointsSelection();
            RestoreMinimizeToTraySelection();

            // event listeners
            WidgetBackgroundColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                WidgetBackgroundColorPicker_SelectedColorChanged);

            GraphColorPicker.RegisterPropertyChangedCallback(
                CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
                GraphColorPicker_SelectedColorChanged);

            _isLoading = false;
        }


        // theme combo box
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

        // behavior settings
        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SettingsService.Instance.MinimizeToTray = MinimizeToTrayToggle.IsOn;
        }
        private void RestoreMinimizeToTraySelection()
        {
            MinimizeToTrayToggle.IsOn = SettingsService.Instance.MinimizeToTray;
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

                    // output to terminal?
                    //System.Diagnostics.Debug.WriteLine($"Polling-Intervall changed to: {newIntervalMs} ms");
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


        // widget combo box
        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                SettingsService.Instance.BackdropType = tag;
            }
        }
        private void ColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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
                ColorSourceComboBox.SelectedIndex = 1;

                SettingsService.Instance.CustomTintColor = colorPicker.SelectedColor;
            }
        }
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
        private void RestoreWidgetSettings()
        {
            ColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseAccentColor ? 0 : 1;

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

            GraphColorSourceComboBox.SelectedIndex = SettingsService.Instance.UseGraphAccentColor ? 0 : 1;
            GraphColorPicker.SelectedColor = SettingsService.Instance.GraphCustomColor;
        }

        // graph data points combo box
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
    }
}