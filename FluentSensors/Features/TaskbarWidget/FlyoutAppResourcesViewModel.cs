using Microsoft.UI.Dispatching;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using FluentSensors.Common.Localization;
using FluentSensors.Core;


namespace FluentSensors.Features.TaskbarWidget
{
    // the app resources of the flyout drawer:
    // cpu, ram and gpu usage of this app as the start page shows them, from AppStatusService and an own
    // WinAppGpuMonitor; live only between Start and Stop, so a closed drawer costs nothing
    public class FlyoutAppResourcesViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        private WinAppGpuMonitor? _gpuMonitor;
        private bool _isGpuReadRunning;

        private string _cpuValue = "-";
        private string _ramValue = "-";
        private string _gpuUsageValue = "-";
        private string _updateIntervalValue = "-";
        private string _readTimeValue = "-";


        // === bindable properties ===

        public string CpuValue
        {
            get => _cpuValue;
            private set { _cpuValue = value; OnPropertyChanged(); }
        }

        public string RamValue
        {
            get => _ramValue;
            private set { _ramValue = value; OnPropertyChanged(); }
        }

        public string GpuUsageValue
        {
            get => _gpuUsageValue;
            private set { _gpuUsageValue = value; OnPropertyChanged(); }
        }

        public string UpdateIntervalValue
        {
            get => _updateIntervalValue;
            private set { _updateIntervalValue = value; OnPropertyChanged(); }
        }

        public string ReadTimeValue
        {
            get => _readTimeValue;
            private set { _readTimeValue = value; OnPropertyChanged(); }
        }


        // === live updates ===

        // idempotent, like Stop
        public void Start()
        {
            if (_gpuMonitor != null) return;

            _gpuMonitor = new WinAppGpuMonitor();
            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;
            ReadGpu();
        }

        public void Stop()
        {
            if (_gpuMonitor == null) return;

            AppStatusService.Instance.StatusUpdated -= OnStatusUpdated;
            _gpuMonitor.Dispose();
            _gpuMonitor = null;
        }

        // the same formats as the start page; (raised on the UI thread)
        private void OnStatusUpdated(AppStatusData data)
        {
            CpuValue = $"{data.CpuUsagePercent:0.0} %";
            RamValue = $"{data.RamUsageBytes / 1024.0 / 1024.0:0} MB";
            UpdateIntervalValue = AppStrings.Format("Start_UpdateIntervalValue", data.ActualUpdateIntervalMs, data.AimedUpdateIntervalMs);
            ReadTimeValue = $"{data.ReadDurationMs:0} ms";

            ReadGpu();
        }

        // off the UI thread at the status cadence, one read at a time
        private void ReadGpu()
        {
            var monitor = _gpuMonitor;
            if (monitor == null || _isGpuReadRunning) return;

            _isGpuReadRunning = true;
            Task.Run(() =>
            {
                AppGpuData? data = null;
                try { data = monitor.Read(); }
                catch { /* the counters vanished; the values stay */ }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    _isGpuReadRunning = false;
                    if (data != null && monitor == _gpuMonitor) GpuUsageValue = $"{data.UsagePercent:0.0} %";
                });
            });
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
