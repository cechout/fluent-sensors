using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Core
{
    // one self status snapshot: sensors found and rendering, the CPU, RAM, handle and GC footprint of this process, the
    // actual and aimed cadence and the read cost
    public record AppStatusData(
        int SensorsFound,
        int SensorsRendered,
        double CpuUsagePercent,
        long RamUsageBytes,
        int HandleCount,
        long GcMemoryBytes,
        double ActualUpdateIntervalMs, // measured, see HardwareMonitorService.ActualUpdateIntervalMs
        int AimedUpdateIntervalMs, // the configured HardwareMonitorService.UpdateIntervalMs
        double ReadDurationMs // see HardwareMonitorService.LastReadDurationMs
    );


    // self monitoring:
    // polls the resource usage of this process and the LHM sensor counts at the sensor polling interval, for the title
    // bar readout and the start page
    public class AppStatusService
    {
        // === fields ===

        private readonly Process _process = Process.GetCurrentProcess();
        private Timer _timer;
        private TimeSpan _lastCpuTime;
        private DateTime _lastSampleTime;

        // HardwareGroups changes on the UI thread only, so Tick reads it there
        private DispatcherQueue _dispatcherQueue;


        // === singleton instance ===

        private static readonly AppStatusService _instance = new AppStatusService();
        public static AppStatusService Instance => _instance;

        private AppStatusService() { }


        // === public api ===

        // once per tick on the UI thread (see Tick)
        public event Action<AppStatusData> StatusUpdated;

        // idempotent
        public void Start()
        {
            if (_timer != null) return;

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _lastCpuTime = _process.TotalProcessorTime;
            _lastSampleTime = DateTime.UtcNow;

            // the interval of HardwareMonitorService, the persisted UpdateIntervalMs
            int intervalMs = HardwareMonitorService.Instance.UpdateIntervalMs;
            _timer = new Timer(_ => Tick(), null, intervalMs, intervalMs);

            HardwareMonitorService.Instance.UpdateIntervalChanged += OnUpdateIntervalChanged;
        }

        public void Stop()
        {
            HardwareMonitorService.Instance.UpdateIntervalChanged -= OnUpdateIntervalChanged;
            _timer?.Dispose();
            _timer = null;
        }


        // === private helpers ===

        // follows a polling rate change
        private void OnUpdateIntervalChanged(int newIntervalMs)
        {
            _timer?.Change(newIntervalMs, newIntervalMs);
        }

        private void Tick()
        {
            // Process caches its values until Refresh
            _process.Refresh();

            var now = DateTime.UtcNow;
            var cpuTime = _process.TotalProcessorTime;

            double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
            double cpuUsedMs = (cpuTime - _lastCpuTime).TotalMilliseconds;

            // percent of the whole machine, like Task Manager
            double cpuPercent = elapsedMs > 0
                ? cpuUsedMs / (elapsedMs * Environment.ProcessorCount) * 100.0
                : 0;

            _lastCpuTime = cpuTime;
            _lastSampleTime = now;

            // HardwareGroups holds the filtered sensors the sensors page shows (no network pseudo adapters, no invalid
            // values), unlike the raw HardwareMonitorService list
            // read on the UI thread, which is the only one mutating it
            _dispatcherQueue.TryEnqueue(() =>
            {
                int sensorsFound = LhmHardwareTreeService.Instance.HardwareGroups.Sum(g => g.Sensors.Count);

                var data = new AppStatusData(
                    SensorsFound: sensorsFound,
                    SensorsRendered: SensorGraphControl.ActiveRenderingCount,
                    CpuUsagePercent: Math.Clamp(cpuPercent, 0, 100),
                    RamUsageBytes: _process.WorkingSet64,
                    HandleCount: _process.HandleCount,
                    GcMemoryBytes: GC.GetTotalMemory(false),
                    ActualUpdateIntervalMs: HardwareMonitorService.Instance.ActualUpdateIntervalMs,
                    AimedUpdateIntervalMs: HardwareMonitorService.Instance.UpdateIntervalMs,
                    ReadDurationMs: HardwareMonitorService.Instance.LastReadDurationMs
                );

                StatusUpdated?.Invoke(data);
            });
        }
    }
}
