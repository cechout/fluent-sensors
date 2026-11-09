using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentSensors.Features.TaskbarWidget;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Persistence.Services;
using FluentSensors.Persistence.Models;
using FluentSensors.Common.Sensors;

class Program
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static string Ids(IEnumerable<SensorGraphViewModel> rows) => string.Join(",", rows.Select(row => row.SensorId));
    static List<SensorRowViewModel> Rows(params string[] ids) => ids.Select(id => new SensorRowViewModel(id)).ToList();
    static void Main()
    {
        var service = SensorSelectionService.Instance;
        service.LoadFromDisk(JsonSerializer.Deserialize<SensorSelectionState>("{\"Taskbar\":[\"cpu\",\"up\",\"down\"]}"));
        var vm = new TaskbarWidgetViewModel(Rows("cpu", "up", "down"));
        Check(Ids(vm.TaskbarSensors) == "cpu,up,down", "Legacy discovery order");
        var cpu = vm.PinnedSensors[0];
        var flyoutCpu = vm.FlyoutSensors[0];
        cpu.History.Add(73);
        vm.SetFlyoutPaused(true);
        service.ToggleCompactTaskbarSensor("cpu");
        service.SetTaskbarSensorInTray("up", true);
        service.SetTaskbarSensorOrder(new[] { "down", "cpu", "up", "down" });
        Check(Ids(vm.PinnedSensors) == "down,cpu,up" && Ids(vm.FlyoutSensors) == "down,cpu,up", "Shared ordering");
        Check(Ids(vm.TaskbarSensors) == "down,cpu", "Mixed placement subset");
        Check(ReferenceEquals(cpu, vm.PinnedSensors[1]) && ReferenceEquals(flyoutCpu, vm.FlyoutSensors[1]) && cpu.History.Single() == 73 && cpu.Frozen, "Reorder retains history/instances/pause");
        vm.Reconfigure(Rows("new", "cpu", "up", "down"));
        Check(Ids(vm.PinnedSensors) == "down,cpu,up,new", "Reconfiguration respects preference and appends new sensors");
        vm.Reconfigure(Rows("cpu", "new"));
        Check(Ids(vm.TaskbarSensors) == "cpu,new", "Deselection");
        vm.Reconfigure(Rows("cpu", "down", "new", "up"));
        Check(Ids(vm.PinnedSensors) == "down,cpu,up,new", "Reconnect restores preferred order");
        Check(service.GetSelection(SensorSelectionProfile.Taskbar).SequenceEqual(new[] { "cpu", "up", "down" }), "Membership unchanged");
        Check(service.IsCompactTaskbarSensor("cpu") && service.IsTaskbarSensorInTray("up"), "Style/placement retained");
        var json = JsonSerializer.Serialize(PersistenceService.Instance.Saved);
        vm.Cleanup();
        service.LoadFromDisk(JsonSerializer.Deserialize<SensorSelectionState>(json));
        var reopened = new TaskbarWidgetViewModel(Rows("cpu", "up", "down"));
        Check(Ids(reopened.TaskbarSensors) == "down,cpu", "Saved order after restart");
        service.SetTaskbarSensorInTray("up", false);
        Check(Ids(reopened.TaskbarSensors) == "down,cpu,up", "Returning sensor uses its position");
        service.SetTaskbarSensorOrder(new[] { "cpu", "down" });
        Check(service.TaskbarSensorOrder.SequenceEqual(new[] { "cpu", "down", "up" }), "Missing IDs retained");
        service.SetTaskbarSensorOrder(Array.Empty<string>());
        Check(Ids(reopened.TaskbarSensors) == "cpu,down,up", "Empty update does not discard saved positions");
        reopened.Cleanup();
        Console.WriteLine("PASS: legacy defaults, duplicate IDs, shared/subset order, persistence/restart, selection/reconnection, graph identity/history/pause, placement and compact-style retention");
    }
}
namespace Microsoft.UI.Dispatching
{
    public class DispatcherQueue
    {
        public static DispatcherQueue GetForCurrentThread() => new();
        public bool TryEnqueue(Action callback) { callback(); return true; }
    }
}
namespace FluentSensors.Common.Sensors
{
    public enum SensorSelectionProfile { WidgetWindow, Csv, Taskbar, WidgetBoard }
    public static class SensorUnitFormatter { public static string Format(double value, string type) => value.ToString(); }
}
namespace FluentSensors.Controls.SensorRow
{
    public class SensorRowViewModel(string id)
    {
        public string Id => id;
        public string Name => id;
        public string SensorType => "Temperature";
        public int HardwareKind => 0;
    }
}
namespace FluentSensors.Controls.SensorGraph
{
    public enum SensorGraphScope { Taskbar, TaskbarFlyout }
    public class SensorGraphViewModel(string id, string name, string type, SensorGraphScope scope, int hardwareKind)
    {
        public string SensorId => id;
        public List<double> History = new();
        public bool Frozen;
        public void SetFrozen(bool value) => Frozen = value;
        public void AddDataPoint(double value, string text) => History.Add(value);
        public void RefreshGraphColor() { }
        public void ResetToBaseline() => History.Clear();
        public void ClearHistory() => History.Clear();
        public void Cleanup() { }
    }
}
namespace FluentSensors.Core
{
    public record SensorData(string Id, double Value, string SensorType);
    public class HardwareMonitorService
    {
        public static HardwareMonitorService Instance = new();
        public event Action<List<SensorData>> HardwareDataUpdated;
    }
}
namespace FluentSensors.Features.TaskbarWidget
{
    internal class SensorTrayIndicators : IDisposable
    {
        internal void Update(IEnumerable<SensorGraphViewModel> sensors) { }
        public void Dispose() { }
    }
}
namespace FluentSensors.Persistence.Services
{
    public class PersistenceService
    {
        public static PersistenceService Instance = new();
        public SensorSelectionState Saved;
        public void SaveSensorSelectionsDebounced(SensorSelectionState state) => Saved = state;
    }
}
