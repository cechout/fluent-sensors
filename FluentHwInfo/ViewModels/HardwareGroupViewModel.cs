using System.Collections.ObjectModel;

namespace FluentHwInfo.ViewModels
{
    /// <summary>
    /// Serves as the mid-level DataContext directly bound to a single UI Expander control, encapsulating the next smaller scope 
    /// in the ViewModel hierarchy
    /// 
    /// Responsibilities:
    /// - Visually groups a specific hardware component (e.g., "Intel Core i9-12900H") by binding its name to the Expander Header.
    /// - Maintains an ObservableCollection of nested SensorRowViewModels bound to the Expander Content.
    /// </summary>
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