using System.Collections.ObjectModel;

namespace FluentHwInfo.ViewModels
{
    public class HardwareGroupViewModel
    {
        // fields
        public string HardwareName { get; set; } = "Hardware Name not provided"; // header of expander
        public ObservableCollection<SensorRowViewModel> Sensors { get; set; } // content of expander

        public HardwareGroupViewModel()
        {
            // initializes the empty list for this specific hardware
            Sensors = new ObservableCollection<SensorRowViewModel>();
        }
    }
}