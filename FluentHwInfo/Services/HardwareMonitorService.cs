using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace FluentHwInfo.Services
{
    public class HardwareMonitorService
    {
        private readonly Computer _computer;
        private IHardware? _cpuHardware;
        private IHardware? _gpuHardware;

        // cpu sensors
        private ISensor? _cpuPackagePowerSensor;
        private ISensor? _cpuIaPowerSensor;
        private ISensor? _cpuGtPowerSensor;

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
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            _computer.Open();
            InitSensors();
        }

        private void InitSensors()
        {
            // search cpu hardware and sensors
            _cpuHardware = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

            if (_cpuHardware != null)
            {
                _cpuPackagePowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"));
                _cpuIaPowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("IA Cores"));
                _cpuGtPowerSensor = _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("GT Cores"));
            }

            // search gpu hardware and sensors
            _gpuHardware = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd);

            if (_gpuHardware != null)
            {
                _gpuPowerSensor = _gpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("GPU Package"));

                // vram is often classified as "small data" in lhm and not as "data", because it is not a constantly changing value like power or temperature,
                // but rather changes in bigger steps. thats why we have to look for the sensor in a different way than the power sensors
                _gpuVramUsedSensor = _gpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"));
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
                if (_cpuGtPowerSensor?.Value != null) CpuGtPowerUpdated?.Invoke(_cpuGtPowerSensor.Value.Value);

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