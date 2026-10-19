using Microsoft.UI.Dispatching;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using FluentSensors.Common;
using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Features.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.WidgetBoard
{
    // the widget on the Windows Widgets board, store build only:
    // keeps the graphs of the pinned sensors, as the taskbar widget keeps its own, and on every tick renders the card
    // of each size the board shows and hands it to FluentSensors.WidgetProvider.exe through the pipe
    // the sensors change on the update button of the sensors page; at start they are the saved board selection, like
    // the restored widget windows
    // nothing is rendered while the board shows no widget of this app
    public sealed partial class WidgetBoardService
    {
        // === fields ===

        private const int ReconnectDelayMs = 2000; // between two tries while the provider is not running

        private DispatcherQueue? _dispatcherQueue;

        // the pipe belongs to the connect thread; a write takes the lock, a failed write closes it
        private readonly object _pipeGate = new();
        private NamedPipeClientStream? _pipe;

        private volatile byte[] _activeSizes = Array.Empty<byte>(); // what the provider reported last
        private int _isSending; // 1 while a card set renders or writes; the tick that finds one running is skipped

        // read on the UI thread, the pipe thread sends them
        private string _notRunningText = "";
        private string _noSensorsText = "";
        private string _linkText = "";


        // === singleton instance ===

        public static WidgetBoardService Instance { get; } = new WidgetBoardService();

        private WidgetBoardService() { }


        // === public api ===

        // after the restores of MainWindow, the sensor rows exist from there
        public void Start()
        {
            if (!AppDistribution.IsPackaged || _dispatcherQueue != null) return;

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _notRunningText = AppStrings.Get("WidgetBoard_NotRunning");
            _noSensorsText = AppStrings.Get("WidgetBoard_NoSensors");
            _linkText = AppStrings.Get("WidgetBoard_SettingsLink");

            RebuildGraphs(ResolveSensors(SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetBoard)));
            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
            SensorsViewModel.Instance.SensorAvailabilityChanged += OnSensorAvailabilityChanged;

            new Thread(ConnectLoop) { IsBackground = true, Name = "widgets board pipe" }.Start();
        }

        // the graphs on the board, in order; the settings page lists them with their y-axis buttons
        public ObservableCollection<SensorGraphViewModel> Graphs { get; } = new();

        public IReadOnlyList<string> PinnedSensorIds => Graphs.Select(g => g.SensorId).ToList();

        // the update button of the sensors page; the board follows at once, not on the next tick
        public void ApplySensors(List<SensorRowViewModel> sensors)
        {
            if (_dispatcherQueue == null) return;

            RebuildGraphs(sensors);
            SendCards();
        }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                foreach (var graph in Graphs)
                {
                    var sensor = payload.FirstOrDefault(s => s.Id == graph.SensorId);
                    if (sensor != null)
                    {
                        graph.AddDataPoint(sensor.Value, SensorUnitFormatter.Format(sensor.Value, sensor.SensorType));
                    }
                }

                SendCards();
            });
        }

        // hardware that left drops out, hardware that came back returns
        private void OnSensorAvailabilityChanged()
        {
            var sensors = ResolveSensors(SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetBoard));
            if (sensors.Count == 0) return;
            if (sensors.Select(s => s.Id).SequenceEqual(Graphs.Select(g => g.SensorId))) return;

            RebuildGraphs(sensors);
        }


        // === graphs ===

        // removes, adds and reorders the graphs to match rows; a kept sensor keeps its history
        private void RebuildGraphs(List<SensorRowViewModel> rows)
        {
            var wanted = rows.Select(r => r.Id).ToHashSet();

            for (int i = Graphs.Count - 1; i >= 0; i--)
            {
                if (wanted.Contains(Graphs[i].SensorId)) continue;
                Graphs[i].Cleanup();
                Graphs.RemoveAt(i);
            }

            var kept = Graphs.ToDictionary(g => g.SensorId);
            Graphs.Clear();
            foreach (var row in rows)
            {
                Graphs.Add(kept.TryGetValue(row.Id, out var graph)
                    ? graph
                    : new SensorGraphViewModel(row.Id, row.Name, row.SensorType, scope: SensorGraphScope.WidgetBoard, hardwareKind: row.HardwareKind));
            }
        }

        // live rows for the saved ids, in discovery order like a live pin
        private static List<SensorRowViewModel> ResolveSensors(IReadOnlyList<string> ids)
        {
            if (ids.Count == 0 || SensorsViewModel.Instance == null) return new List<SensorRowViewModel>();

            var wanted = new HashSet<string>(ids);
            return SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors))
                .Where(s => wanted.Contains(s.Id))
                .ToList();
        }


        // === cards ===

        // a snapshot on the UI thread, rendered and written on a pool thread
        private void SendCards()
        {
            byte[] sizes = _activeSizes;
            if (sizes.Length == 0 || _pipe == null) return;
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0) return;

            var snapshot = Graphs.Select(TakeSnapshot).ToList();
            bool fillFade = SettingsService.Instance.GraphFillFade;
            var lineStyle = SettingsService.Instance.GraphLineStyle;
            string timeText = AppStrings.Format("TimeRange_Last", SettingsService.Instance.WidgetBoardGraphTimeSpanSeconds);
            string noSensorsText = _noSensorsText;
            string linkText = _linkText;

            Task.Run(() =>
            {
                try
                {
                    float scale = Math.Max(2f, GetDpiForSystem() / 96f);

                    foreach (byte size in sizes)
                    {
                        int count = Math.Min(WidgetBoardCard.SensorSlots(size), snapshot.Count);
                        if (count == 0)
                        {
                            var (noteTemplate, noteData) = WidgetBoardCard.BuildNote(noSensorsText);
                            WriteCard(size, noteTemplate, noteData);
                            continue;
                        }

                        var (width, height) = WidgetBoardCard.GraphBox(size, count);

                        var sensors = snapshot.Take(count).Select(s => new WidgetBoardCardSensor(
                            s.Name,
                            s.Value,
                            WidgetBoardRenderer.Render(s.Values, s.YBottom, s.YTop, s.Dark, fillFade, lineStyle, false, width, height, scale),
                            WidgetBoardRenderer.Render(s.Values, s.YBottom, s.YTop, s.Light, fillFade, lineStyle, true, width, height, scale)))
                            .ToList();

                        WriteCard(size, WidgetBoardCard.BuildTemplate(count), WidgetBoardCard.BuildData(sensors, timeText, linkText));
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isSending, 0);
                }
            });
        }

        private sealed record GraphSnapshot(string Name, string Value, double?[] Values, double YBottom, double YTop, SKColor Dark, SKColor Light);

        private static GraphSnapshot TakeSnapshot(SensorGraphViewModel graph)
        {
            var values = graph.SensorData.ToArray();
            var (bottom, top) = ResolveYRange(graph, values);
            var dark = graph.GraphColor.Dark;
            var light = graph.GraphColor.Light;

            return new GraphSnapshot(
                graph.SensorName,
                graph.CurrentValueText,
                values,
                bottom,
                top,
                new SKColor(dark.R, dark.G, dark.B),
                new SKColor(light.R, light.G, light.B));
        }

        // the range SensorGraphControl.ApplyYRange sets: the manual limit, or 0 to the highest point (a series below
        // zero hangs from 0), 0 to 1 while every value is 0
        private static (double Bottom, double Top) ResolveYRange(SensorGraphViewModel graph, double?[] values)
        {
            if (!graph.IsAutoScaled) return graph.ManualYMax < 0 ? (graph.ManualYMax, 0.0) : (0.0, graph.ManualYMax);

            double lowest = 0, highest = 0;
            foreach (var value in values)
            {
                if (!value.HasValue) continue;
                if (value.Value > highest) highest = value.Value;
                else if (value.Value < lowest) lowest = value.Value;
            }

            if (highest > 0) return (0, highest);
            if (lowest < 0) return (lowest, 0);
            return (0, 1);
        }


        // === pipe ===

        // connects whenever the provider runs (the board starts it with its first widget), tells it the not running
        // text and then listens for the sizes it shows
        private void ConnectLoop()
        {
            while (true)
            {
                NamedPipeClientStream? pipe = null;
                try
                {
                    // no CurrentUserOnly here: it compares the pipe owner with the owner of this token, which is the
                    // administrators group for an elevated process; the provider limits the pipe to the user
                    pipe = new NamedPipeClientStream(".", WidgetBoardProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipe.Connect(0);

                    lock (_pipeGate)
                    {
                        _pipe = pipe;
                        var writer = new BinaryWriter(pipe);
                        writer.Write(WidgetBoardProtocol.NotRunningText);
                        writer.Write(_notRunningText);
                        writer.Flush();
                    }

                    var reader = new BinaryReader(pipe);
                    while (true)
                    {
                        switch (reader.ReadByte())
                        {
                            case WidgetBoardProtocol.ActiveSizes:
                                _activeSizes = reader.ReadBytes(reader.ReadByte());
                                _dispatcherQueue?.TryEnqueue(SendCards); // a widget that just appeared fills at once
                                break;

                            case WidgetBoardProtocol.ActionInvoked:
                                if (reader.ReadString() == WidgetBoardProtocol.OpenSettingsVerb)
                                {
                                    _dispatcherQueue?.TryEnqueue(OpenSettings);
                                }
                                break;

                            default:
                                throw new InvalidDataException("unknown message from the provider");
                        }
                    }
                }
                catch
                {
                    // no provider running, or it went away; (an exception left on this thread would end the app)
                }
                finally
                {
                    _activeSizes = Array.Empty<byte>();
                    lock (_pipeGate)
                    {
                        if (_pipe == pipe) _pipe = null;
                    }
                    pipe?.Dispose();
                }

                Thread.Sleep(ReconnectDelayMs);
            }
        }

        // the settings link of the card: the widgets board section, a closed main window opens again
        private static void OpenSettings()
        {
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenWidgetBoardSettings();
            }
            else
            {
                var mainWindow = new MainWindow();
                mainWindow.Activate();
                mainWindow.OpenWidgetBoardSettings();
            }
        }

        private void WriteCard(byte size, string template, string data)
        {
            lock (_pipeGate)
            {
                if (_pipe == null) return;

                try
                {
                    var writer = new BinaryWriter(_pipe);
                    writer.Write(WidgetBoardProtocol.Card);
                    writer.Write(size);
                    writer.Write(template);
                    writer.Write(data);
                    writer.Flush();
                }
                catch (IOException)
                {
                    _pipe.Dispose(); // the read in ConnectLoop fails next and reconnects
                    _pipe = null;
                }
            }
        }


        // === win32 api imports ===

        [LibraryImport("user32.dll")]
        private static partial uint GetDpiForSystem();
    }
}
