using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using FluentSensors.Controls.SensorGraph;

namespace FluentSensors.Features.TaskbarWidget
{
    // Each notification-area slot is square. Reserve its height for the value; the full label and unit
    // stay in the tooltip. Windows controls their order and whether they are in the overflow.
    internal sealed class SensorTrayIndicators : IDisposable
    {
        private sealed class Entry : IDisposable
        {
            internal readonly TrayIcon Tray;
            internal Icon? Image;
            internal string LastText = "";
            private readonly DispatcherQueue _queue;
            private readonly Action _clicked;
            private bool _disposed;
            internal Entry(string sensorId, Icon image, Action clicked)
            {
                _queue = DispatcherQueue.GetForCurrentThread();
                _clicked = clicked;
                Image = image;
                // The default constructor uses one entry-assembly identity for every icon.
                Tray = new TrayIcon(TrayIcon.CreateUniqueGuidForProcessPath("FluentSensors.Sensor." + sensorId));
                Tray.UpdateIcon(image.Handle);
                Tray.UseStandardTooltip = true;
                Tray.MessageWindow.MouseEventReceived += OnMouse;
                Tray.MessageWindow.TaskbarCreated += OnTaskbarCreated;
                Tray.Create();
            }
            private void OnMouse(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
            {
                if (e.MouseEvent == MouseEvent.IconLeftMouseUp)
                    _queue.TryEnqueue(() => { if (!_disposed) _clicked(); });
            }
            private void OnTaskbarCreated(object? sender, EventArgs e) =>
                _queue.TryEnqueue(() => { if (!_disposed) { Tray.TryRemove(); Tray.Create(); } });
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Tray.MessageWindow.MouseEventReceived -= OnMouse;
                Tray.MessageWindow.TaskbarCreated -= OnTaskbarCreated;
                Tray.Dispose();
                Image?.Dispose();
            }
        }

        private readonly Dictionary<string, Entry> _entries = new();

        internal void Update(IEnumerable<SensorGraphViewModel> sensors)
        {
            var current = sensors.ToList();
            var ids = current.Select(s => s.SensorId).ToHashSet();
            foreach (var id in _entries.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _entries[id].Dispose();
                _entries.Remove(id);
            }
            foreach (var sensor in current)
            {
                bool light = Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Light;
                var foreground = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                // Opaque gray keeps the native icon background visible through the shell icon mask.
                var background = Windows.UI.Color.FromArgb(255, 55, 58, 60);
                if (!_entries.TryGetValue(sensor.SensorId, out var entry))
                {
                    entry = new Entry(sensor.SensorId, Render(sensor, light, foreground, background), () =>
                    {
                        var widget = TaskbarWidgetWindow.CurrentInstance;
                        if (widget != null) TaskbarFlyoutWindow.ToggleFromTray(widget);
                    });
                    _entries.Add(sensor.SensorId, entry);
                }
                string value = sensor.CurrentValueText;
                string tooltip = $"{sensor.SensorName}: {value}";
                entry.Tray.UpdateToolTip(tooltip.Length > 127 ? tooltip[..127] : tooltip);
                string key = value + light + foreground.ToString() + background.ToString();
                if (key == entry.LastText) continue;
                var image = Render(sensor, light, foreground, background);
                entry.Tray.UpdateIcon(image.Handle);
                entry.Image?.Dispose();
                entry.Image = image;
                entry.LastText = key;
            }
        }

        private static Icon Render(SensorGraphViewModel sensor, bool light, Windows.UI.Color foreground, Windows.UI.Color background) =>
            SensorTrayIconRenderer.Render(sensor.CurrentValueText,
                sensor.SensorData.Count > 0 ? sensor.SensorData.Last() ?? 0 : 0, sensor.SensorType, light,
                Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B),
                Color.FromArgb(background.A, background.R, background.G, background.B));
        public void Dispose()
        {
            foreach (var entry in _entries.Values) entry.Dispose();
            _entries.Clear();
        }

    }
}
