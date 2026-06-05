using FluentHwInfo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace FluentHwInfo.Views
{
    public sealed partial class SensorsPage : Page
    {
        // here we define the ViewModel SensorViewModel as a property of the SensorsPage class, which is the DataContext for
        // the whole XAML page
        // so {x:Bind ViewModel.HardwareGroups} can find its target
        public SensorsViewModel ViewModel { get; }

        // we remember the currently open widget window (spam protection)
        private static WidgetWindow _currentWidgetWindow = null;

        public SensorsPage()
        {
            this.InitializeComponent();

            // change: we do not create a new ViewModel anymore
            // we bind the UI simply to the immortal, central Singleton instance
            ViewModel = SensorsViewModel.Instance;
        }

        private void PinToWidget_Click(object sender, RoutedEventArgs e)
        {
            // flatten the nested groups and filter for selected items
            // this is LINQ, which is a more concise way to write the same logic as the long form below
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

            // the long form without LINQ would look like this:
            //List<SensorRowViewModel> selectedSensors = new List<SensorRowViewModel>();

            //// 1. go through each hardware group "the boxes"
            //foreach (var group in ViewModel.HardwareGroups)
            //{
            //    // 2. go through each sensor in this group (the contents of the box); that is what is known as "flattening"
            //    foreach (var sensor in group.Sensors)
            //    {
            //        // 3. check if the checkbox is set; that is the ".Where"
            //        if (sensor.IsSelected == true)
            //        {
            //            // 4. pack it in our final list; that is the ".ToList()"
            //            selectedSensors.Add(sensor);
            //        }
            //    }
            //}

            // prevent window creation if no sensors are selected
            if (selectedSensors.Count == 0) return;

            // if the window is already open, we force it to close
            // the null-conditional operator (?.) only calls Close() if it is not null.
            _currentWidgetWindow?.Close();

            // we rebuild build the window completely fresh with the new data in any case
            _currentWidgetWindow = new WidgetWindow(selectedSensors);
            // the journey of selectedSensors: SensorsPage (View) -> WidgetWindow (View) -> WidgetViewModel (ViewModel)

            // we let the new WidgetWindow instance register a closed event handler to itself
            // why do we do this? because if the user manually closes the widget window, this _currentWidgetWindow variable
            // would still point to the old (now closed) window
            // it the user would then click on "Pin to Widget" again, the app would think the old window is still open and
            // would try to close it again, which would throw an exception because the old window is already closed
            _currentWidgetWindow.Closed += (s, args) =>
            {
                _currentWidgetWindow = null;
            };

            _currentWidgetWindow.Activate();
        }
    }
}