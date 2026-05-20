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
        private ISensor? _cpuPackagePowerSensor; // ? in case the sensor is not found
        private CancellationTokenSource _cts;
        public int UpdateIntervalMs { get; set; } = 500; // standardvalue = 500ms

        // full form would look like this:
        // private int _updateIntervalMs = 500;
        // public int GetUpdateIntervalMs()
        // {
        //     return _updateIntervalMs;
        // }
        // public void SetUpdateIntervalMs(int value)
        // {
        //     _updateIntervalMs = value;
        // }

        // this is a function that works with the publisher-subscriber-principle
        // other classes can "subscribe" to this event and get notified when the event is "fired"
        // it returs the double value we get from the sensor
        public event Action<double> CpuPowerUpdated;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true, // for now we just want to monitor the cpu
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            _computer.Open();
            initSensor();
        }

        private void initSensor()
        {
            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu == null) return;

            // for my i9 12900k the sensor is called something like "CPU Power", and could contain also the word "Package"
            _cpuPackagePowerSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"));
        }

        public void StartMonitoring()
        {
            if (_cpuPackagePowerSensor == null) return;

            _cts = new CancellationTokenSource();
            _ = loopAsync(_cts.Token);
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
        }

        private async Task loopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _cpuPackagePowerSensor?.Hardware.Update(); // tell the hardware to pull new values

                if (_cpuPackagePowerSensor != null && _cpuPackagePowerSensor.Value.HasValue)
                {
                    CpuPowerUpdated?.Invoke(_cpuPackagePowerSensor.Value.Value); // fire event and pass the value
                }

                await Task.Delay(UpdateIntervalMs, token); // wait until the interval is over
            }
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }
    }
}
