using FluentHwInfo.Services;
using System.Collections.ObjectModel;

namespace FluentHwInfo.ViewModels
{
    /// <summary>
    /// Serves as the primary "Parent-ViewModel" and State Manager for the CPU monitoring view (CPUPage.xaml).
    /// 
    /// Responsibilities:
    /// - Initializes and maintains an ObservableCollection of SensorRowViewModel instances, 
    ///   which serves as the dynamic data source for the XAML ListView
    /// - Acts as the architectural bridge between the backend service and the frontend UI state
    /// - Instantiates the HardwareMonitorService and subscribes to its hardware update events (Publisher-Subscriber pattern).
    /// 
    /// Data Flow:
    /// When a hardware event fires (e.g., CpuPackagePowerUpdated), this class intercepts the raw double value and routes it 
    /// directly to the SensorRowViewModel.UpdateValue(double) method of the corresponding child row.
    /// </summary>
    public class CPUViewModel
    {
        // ObservableCollection is a smart list that automatically notifies the UI
        // when you add, remove or change items in the list
        public ObservableCollection<SensorRowViewModel> SensorList { get; set; }

        // we create here the HardwareMonitorService
        private HardwareMonitorService _service;

        // this are our 3 metric rows for the ListView
        private SensorRowViewModel _packageRow;
        private SensorRowViewModel _iaCoresRow;
        private SensorRowViewModel _iGpuRow;

        public CPUViewModel()
        {
            SensorList = new ObservableCollection<SensorRowViewModel>();

            // here we simply create our 3 rows at the for the list
            // this replaces all the hardcoded <ListViewItem> from the XAML
            _packageRow = new SensorRowViewModel { Name = "CPU Package Power" };
            _iaCoresRow = new SensorRowViewModel { Name = "IA Cores Power" };
            _iGpuRow = new SensorRowViewModel { Name = "iGPU Power" };

            SensorList.Add(_packageRow);
            SensorList.Add(_iaCoresRow);
            SensorList.Add(_iGpuRow);

            // initialize the hardware monitor service
            _service = new HardwareMonitorService();

            // 3. SUBSCRIBE TO EVENTS (the publisher-subscriber principle in action)
            // when the service fires, we simply forward the value (val) to the row
            _service.CpuPackagePowerUpdated += (val) => _packageRow.UpdateValue(val);
            _service.CpuIaPowerUpdated += (val) => _iaCoresRow.UpdateValue(val);
            _service.CpuGtPowerUpdated += (val) => _iGpuRow.UpdateValue(val);

            // 4. start monitoring
            _service.StartMonitoring();
        }
    }
}