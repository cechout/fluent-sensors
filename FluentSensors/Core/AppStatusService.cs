using System;
using System.Diagnostics;
using System.Threading;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Core
{
    // one snapshot of the apps self status: how many sensors LHM found and how many of them are currently
    // rendering, plus this processes own CPU/RAM/handle/GC footprint
    public record AppStatusData(
        int SensorsFound,
        int SensorsRendered,
        double CpuUsagePercent,
        long RamUsageBytes,
        int HandleCount,
        long GcMemoryBytes
    );


    // self monitoring: polls this processes own resource usage plus the LHM sensor counts on a fixed interval
    // feeds the title bar status readout for now, the future App Status page reuses the same data
    public class AppStatusService
    {
        // === fields ===

        private const int UpdateIntervalMs = 1000;

        private readonly Process _process = Process.GetCurrentProcess();
        private Timer _timer;
        private TimeSpan _lastCpuTime;
        private DateTime _lastSampleTime;


        // === singleton instance ===

        private static readonly AppStatusService _instance = new AppStatusService();
        public static AppStatusService Instance => _instance;

        private AppStatusService() { }


        // === public api ===

        // fires once per tick with a fresh snapshot; always on a background thread, consumers marshal to the UI
        // thread themselves (same contract as HardwareMonitorService.HardwareDataUpdated)
        public event Action<AppStatusData> StatusUpdated;

        // starts the polling timer; safe to call more than once, later calls are a no-op
        public void Start()
        {
            if (_timer != null) return;

            _lastCpuTime = _process.TotalProcessorTime;
            _lastSampleTime = DateTime.UtcNow;

            _timer = new Timer(_ => Tick(), null, UpdateIntervalMs, UpdateIntervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }


        // === private helpers ===

        private void Tick()
        {
            // Process caches its own snapshot until Refresh is called, without this CpuTime/WorkingSet64/
            // HandleCount below would all keep returning the values from process start
            _process.Refresh();

            var now = DateTime.UtcNow;
            var cpuTime = _process.TotalProcessorTime;

            double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
            double cpuUsedMs = (cpuTime - _lastCpuTime).TotalMilliseconds;

            // percent relative to the whole machine (all cores), matches how the modern Task Manager shows it
            double cpuPercent = elapsedMs > 0
                ? cpuUsedMs / (elapsedMs * Environment.ProcessorCount) * 100.0
                : 0;

            _lastCpuTime = cpuTime;
            _lastSampleTime = now;

            var data = new AppStatusData(
                SensorsFound: HardwareMonitorService.Instance.TotalSensorsFound,
                SensorsRendered: SensorGraphControl.ActiveRenderingCount,
                CpuUsagePercent: Math.Clamp(cpuPercent, 0, 100),
                RamUsageBytes: _process.WorkingSet64,
                HandleCount: _process.HandleCount,
                GcMemoryBytes: GC.GetTotalMemory(false)
            );

            StatusUpdated?.Invoke(data);
        }
    }
}
