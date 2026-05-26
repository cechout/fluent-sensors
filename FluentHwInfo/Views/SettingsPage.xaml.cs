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
        }

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

        private void IntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null && int.TryParse(selectedItem.Tag.ToString(), out int newIntervalMs))
                {
                    // TODO: Pass the new milliseconds value (newIntervalMs) to your HardwareMonitorService.
                    // Tip for later: Since your SensorsViewModel currently creates its own local instance of the service,
                    // it makes sense to provide the service as an app-wide singleton (e.g., in App.xaml.cs),
                    // so that the SettingsPage can throttle or accelerate the same engine.
                    System.Diagnostics.Debug.WriteLine($"Engine-Polling-Intervall geändert auf: {newIntervalMs} ms");
                }
            }
        }
    }
}