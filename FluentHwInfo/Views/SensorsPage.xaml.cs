using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using FluentHwInfo.ViewModels;

namespace FluentHwInfo.Views
{
    public sealed partial class SensorsPage : Page
    {
        // here we define the ViewModel SensorViewModel as a property of the SensorsPage class, which is the DataContext for
        // the whole XAML page
        // so {x:Bind ViewModel.HardwareGroups} can find its target
        public SensorsViewModel ViewModel { get; }

        public SensorsPage()
        {
            this.InitializeComponent();

            // here we create the highest ViewModel
            // Once this happens, the HardwareMonitorService automatically starts its 500ms measurement loop in the background
            ViewModel = new SensorsViewModel();
        }

        private void PinToWidget_Click(object sender, RoutedEventArgs e)
        {
            // The logic to send the values to the desktop widget will go here later
        }
    }
}