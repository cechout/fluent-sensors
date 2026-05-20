using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace FluentHwInfo.Services
{
    /// <summary>
    /// Acts as the dedicated backend engine for reading raw physical hardware metrics, utilizing the LibreHardwareMonitorLib.
    /// 
    /// Responsibilities:
    /// - Initializes hardware access and targets specific components (e.g., CPU, GPU) while explicitly disabling irrelevant 
    ///   hardware (like Storage or Network) to maximize polling performance
    /// - Runs an isolated, asynchronous polling loop governed by a CancellationTokenSource to continuously read sensor values at 
    ///   a configurable interval
    /// - Broadcasts raw double values to the rest of the application using C# Events (e.g., Action{T}).
    /// 
    /// Architecture Constraints:
    /// Adheres strictly to the Single Responsibility Principle. This class has absolute zero knowledge of ViewModels, UI elements, 
    /// data persistence, or string formatting. It exclusively handles hardware communication and blind data broadcasting
    /// </summary>
    public class HardwareMonitorService
    {
        private readonly Computer _computer;

        // hardware components
        private IHardware? _cpuHardware;
        private IHardware? _gpuHardware;
        private IHardware? _iGpuHardware;

        // cpu sensors
        private ISensor? _cpuPackagePowerSensor;
        private ISensor? _cpuIaPowerSensor;

        private ISensor? _cpuLoadSensor;

        // intel iris
        private ISensor? _iGpuPowerSensor;

        // gpu sensors
        private ISensor? _gpuPowerSensor;
        private ISensor? _gpuVramUsedSensor;

        private CancellationTokenSource? _cts;

        public int UpdateIntervalMs { get; set; } = 500;
        // full form would look like this:
        // private int _updateIntervalMs = 500; // field
        // public int GetUpdateIntervalMs() // property
        // {
        //     return _updateIntervalMs;
        // }
        // public void SetUpdateIntervalMs(int value)
        // {
        //     _updateIntervalMs = value;
        // }

        // events for all your new data
        // this is a function that works with the publisher-subscriber-principle
        // other classes can "subscribe" to this event and get notified when the event is "fired"
        // it returns the double value we get from the sensor
        public event Action<double>? CpuPackagePowerUpdated;
        public event Action<double>? CpuIaPowerUpdated;
        public event Action<double>? CpuGtPowerUpdated;
        public event Action<double>? GpuPowerUpdated;
        public event Action<double>? GpuVramUsedUpdated;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true, 
                IsMemoryEnabled = false,
                IsMotherboardEnabled = true,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            _computer.Open();
            InitSensors();
        }

        private void InitSensors()
        {
            // CPU HARDWARE
            _cpuHardware = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (_cpuHardware != null)
            {
                _cpuLoadSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("CPU Total"));

                _cpuPackagePowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("CPU Package"));
                _cpuIaPowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("CPU Cores"));
            }

            // INTEL IRIS 
            _iGpuHardware = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
            if (_cpuHardware != null)
            {
                _iGpuPowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("GPU Power"));
            }

            // GPU HARDWARE
            _gpuHardware = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd);
            if (_gpuHardware != null)
            {
                _gpuPowerSensor = _gpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("GPU Package"));
                _gpuVramUsedSensor = _gpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Used"));
            }
        }

        public void StartMonitoring()
        {
            // prevent double execution
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _ = LoopAsync(_cts.Token);
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // instead of updating individual sensors, we simply update the entire hardware component at once
                _cpuHardware?.Update();
                _gpuHardware?.Update();

                if (_cpuPackagePowerSensor?.Value != null) CpuPackagePowerUpdated?.Invoke(_cpuPackagePowerSensor.Value.Value);
                if (_cpuIaPowerSensor?.Value != null) CpuIaPowerUpdated?.Invoke(_cpuIaPowerSensor.Value.Value);
                if (_iGpuPowerSensor?.Value != null) CpuGtPowerUpdated?.Invoke(_iGpuPowerSensor.Value.Value);

                if (_gpuPowerSensor?.Value != null) GpuPowerUpdated?.Invoke(_gpuPowerSensor.Value.Value);

                // vram is often in megabytes (MB) in lhm. we divide by 1024 to provide GB directly to the app
                if (_gpuVramUsedSensor?.Value != null) GpuVramUsedUpdated?.Invoke(Math.Round(_gpuVramUsedSensor.Value.Value / 1024.0, 2));

                await Task.Delay(UpdateIntervalMs, token);
            }
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }

    }
}