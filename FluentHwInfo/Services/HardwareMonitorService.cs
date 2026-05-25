using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluentHwInfo.Services
{
    /// <summary>
    /// this is the record container for all the relevant data about one sensor
    /// a record is like a struct in c, it behaves like a class but the objects are read only
    /// so we can create new objects with new values but we cannot change the values of an existing object
    /// we gonne send a list of these records everry iteration to the UI
    /// it contains everything the ui needs to know about a sensor
    /// </summary>
    public record SensorData(
        string Id,           // e.g. "/intelcpu/0/load/1" -> this is gonne be the idenifier for the settings later
        string Name,         // e.g. "CPU Package"
        string HardwareName, // e.g. "Intel Core i9-12900H"
        string SensorType,   // e.g. "Power", "Temperature", "Load"
        double Value         // the actual value of the sensor
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
        // it contains all the sensors we want to monitor
        // the manual way would be:
        // "private IHardware? _cpuHardware;" 
        // "private ISensor? _cpuPackagePowerSensor;" and so on
        private readonly List<ISensor> _activeSensors = new();

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

        // the master event
        // instead of having multiple events for each sensor, we can have one event that
        // sends a list of all the sensor data at once
        // the manual way would be:
        // "public event Action<double>? CpuPackagePowerUpdated;"
        // "public event Action<double>? CpuIaPowerUpdated;" and so on
        public event Action<List<SensorData>>? HardwareDataUpdated;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,

            };

            _computer.Open();
            InitAllSensors();
        }

        private void InitAllSensors()
        {
            _activeSensors.Clear();

            // we just go though all the hardware components that lhm detects
            foreach (var hardware in _computer.Hardware)
            {
                DiscoverSensors(hardware);
            }
        }

        private void DiscoverSensors(IHardware hardware)
        {
            // pump all relevant sensors of this found hardware into our flat list
            // "var sensor in hardware.Sensors" is the short form of
            // "ISensor in hardware.Sensors", we don't need to write the type of the variable if it can
            // be inferred from the right side of the assignment
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

        // "async Task" is a method that runs asynchronously and can be awaited
        // so the program continues to run every other thing and only after exactly the set interval
        // it will get executed again, this is the perfect way to create a loop that runs every x milliseconds without
        // blocking the main thread
        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // update hardware (lhm fetches new values from the sensor)
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                }

                // this is the exact list for our big event HardwareDataUpdated, we create a new list
                // and every iteration and fill it with the current values of all the sensors we want to monitor
                var payload = new List<SensorData>();

                foreach (var sensor in _activeSensors)
                {
                    // some sensors might not have a value at the moment (e.g., if an HDD is sleeping)
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

                // we fire the event with the new list of sensor data, all the subscribers (e.g., the ViewModels)
                // will receive this list and can do whatever they want with it (e.g., update the UI, ...)
                HardwareDataUpdated?.Invoke(payload);

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