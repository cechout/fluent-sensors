using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluentHwInfo.Services
{
    // record container for all the relevant data about one sensor
    public record SensorData(
        string Id, // e.g. "/intelcpu/0/load/1" 
        string Name, // e.g. "CPU Package"
        string HardwareName, // e.g. "Intel Core i9-12900H"
        string SensorType, // e.g. "Power", "Temperature", "Load"
        double Value // the actual value of the sensor
    );

    /// <summary>
    /// Acts as the dedicated backend engine for reading raw physical hardware metrics, utilizing the 
    /// LibreHardwareMonitorLib
    /// 
    /// Responsibilities:
    /// - Initializes hardware access dynamically across all major core components including CPU, GPUs (dedicated and 
    ///   integrated), Memory, and Storage devices
    /// - Performs recursive hardware and sub-hardware discovery to build a flat, optimized list of active telemetry 
    ///   sensors
    /// - Runs an isolated, asynchronous polling loop governed by a CancellationTokenSource to continuously refresh 
    ///   sensor values at a configurable interval without blocking the UI thread
    /// - Packages all active sensor metrics into a unified, lightweight, and immutable data payload (SensorData records)
    ///   and broadcasts it via a single master event
    /// 
    /// Architecture Constraints:
    /// Adheres strictly to the Single Responsibility Principle. This class has absolute zero knowledge of ViewModels, 
    /// UI elements, data persistence, or string formatting. It exclusively handles hardware communication and 
    /// blind data broadcasting
    /// </summary>
    public class HardwareMonitorService
    {
        private readonly Computer _computer;

        // the dynamic list
        // it contains all sensors we want to monitor
        // the manual way would be:
        // "private IHardware? _cpuHardware;" 
        // "private ISensor? _cpuPackagePowerSensor;" and so on
        private readonly List<ISensor> _activeSensors = new();

        private readonly object _sensorLock = new object();
        private CancellationTokenSource? _cts;
        public int UpdateIntervalMs { get; set; } = 500;

        // the master event
        // instead of having multiple events for each sensor, we can have one event that
        // sends a list of all the sensor data at once
        // the manual way would be:
        // "public event Action<double>? CpuPackagePowerUpdated;"
        // "public event Action<double>? CpuIaPowerUpdated;" and so on
        public event Action<List<SensorData>>? HardwareDataUpdated;

        // now we make this class a singleton, because we want to have only one instance of this service that runs in the
        // background and updates the sensor values
        private static readonly HardwareMonitorService _instance = new HardwareMonitorService();
        public static HardwareMonitorService Instance => _instance;

        private HardwareMonitorService()
        {
            _computer = new Computer
            {
                // all hardware components are explicitly disabled here to prevent the UI thread from freezing 
                // the actual initialization is deferred and chunked into the asynchronous pipeline methods (Init...Async) below
                IsCpuEnabled = false,
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,

            };

            _computer.Open();
        }


        // asynchronous initialization pipeline
        // lhm heavily blocks the calling thread when enabling all the hardware components
        // to prevent application freezes, these methods allow any consuming class or caller to trigger the 
        // hardware discovery step-by-step on isolated background threads (via Task.Run)
        public Task InitMotherboardAsync()
        {
            return Task.Run(() => { _computer.IsMotherboardEnabled = true; });
        }
        public Task InitCpuAsync()
        {
            return Task.Run(() => { _computer.IsCpuEnabled = true; });
        }
        public Task InitGpuAsync()
        {
            return Task.Run(() => { _computer.IsGpuEnabled = true; });
        }
        public Task InitMemoryAndStorageAsync()
        {
            return Task.Run(() =>
            {
                _computer.IsMemoryEnabled = true;
                _computer.IsStorageEnabled = true;
            });
        }

        // starts the background polling loop to read sensor values
        // this method gets called from the outside (e.g. MainWindow); only after the asynchronous initialization pipeline has
        // fully completed of course
        public void StartMonitoring()
        {
            // prevent double execution
            if (_cts != null) return;

            InitAllSensors();
            _cts = new CancellationTokenSource();

            // task.run() creates a new thread in the background, and puts explicitly the method
            // LoopAsync on this new thread
            Task.Run(() => LoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _cts = null;
        }

        public void Cleanup()
        {
            StopMonitoring();
            _computer.Close();
        }


        // private class methods
        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // update hardware (lhm fetches new values from the sensor)
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                }

                // this is the exact list for the big event HardwareDataUpdated, we create a new list
                // and every iteration fill it with the current values of all the sensors we want to monitor
                var payload = new List<SensorData>();

                lock (_sensorLock)
                {
                    foreach (var sensor in _activeSensors)
                    {
                        // some sensors might not have a value at the moment (maybe a hdd is still sleeping or smth)
                        if (sensor.Value.HasValue)
                        {
                            payload.Add(new SensorData(
                                Id: sensor.Identifier.ToString(),
                                Name: sensor.Name,
                                HardwareName: sensor.Hardware.Name,
                                SensorType: sensor.SensorType.ToString(),
                                Value: sensor.Value.Value
                            ));
                        }
                    }
                }

                // we fire the event with the new list of sensor data
                HardwareDataUpdated?.Invoke(payload);

                // await Task.Delay() does not freeze the thread in the same way as Thread.Sleep(), 
                // it saves the state of the method and returns the background thread to the windows thread pool
                // for those few milliseconds, and after the delay, it grabs a free thread again from the windows
                // thread pool and continues the execution of the method from where it left off
                await Task.Delay(UpdateIntervalMs, token);
            }
        }

        // goes through the discovered hardware tree and registers relevant sensors into the flat list
        // this process is protected by _sensorLock to ensure thread-safety, preventing collection modification crashes if the
        // background polling loop is preparing to run simultaneously
        private void InitAllSensors()
        {
            lock (_sensorLock)
            {
                _activeSensors.Clear();

                // we go through every sensor that lhm detects
                foreach (var hardware in _computer.Hardware)
                {
                    DiscoverSensors(hardware);
                }
            }
        }

        private void DiscoverSensors(IHardware hardware)
        {
            // pump all relevant sensors of this found hardware into our flat list
            foreach (var sensor in hardware.Sensors)
            {
                // we accepts only this explicit sensor types, all the other are not relevant for now
                if (sensor.SensorType == SensorType.Load ||
                    sensor.SensorType == SensorType.Power ||
                    sensor.SensorType == SensorType.Temperature ||
                    sensor.SensorType == SensorType.Clock ||
                    sensor.SensorType == SensorType.Data ||
                    sensor.SensorType == SensorType.SmallData)
                {
                    _activeSensors.Add(sensor);
                }
            }

            // some hardware (like motherboards or big GPUs) have sub-hardware
            // we traverse them recursively here
            foreach (var subHardware in hardware.SubHardware)
            {
                DiscoverSensors(subHardware);
            }
        }
    }
}