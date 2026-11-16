using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI.Icons;


namespace FluentSensors.Common.UI
{
    // the source icon:
    // A small icon behind a sensor title that says who measured the value, LibreHardwareMonitor or Windows
    // the two marks are drawn in AppIcons
    public static class SensorSourceIcon
    {
        // every Path that got a sensor, so a switched setting reaches the ones on screen; weak, panels come and go,
        // and from every window thread
        private static readonly List<WeakReference<Path>> _paths = new();
        private static readonly object _pathsLock = new();
        private const int PruneEvery = 256;
        private static int _registrationsSincePrune;


        // === settings mirror ===

        // whether the icon shows for the sensors of each source; SettingsService persists them and mirrors them in
        // here
        // (like SensorUnitFormatter.DataSizeBasis)
        public static bool IsLhmIconVisible { get; private set; } = true;
        public static bool IsWindowsIconVisible { get; private set; } = true;

        public static void SetVisibility(bool isLhmIconVisible, bool isWindowsIconVisible)
        {
            if (IsLhmIconVisible == isLhmIconVisible && IsWindowsIconVisible == isWindowsIconVisible) return;

            IsLhmIconVisible = isLhmIconVisible;
            IsWindowsIconVisible = isWindowsIconVisible;

            List<Path> paths;
            lock (_pathsLock)
            {
                paths = new List<Path>();
                foreach (var reference in _paths)
                {
                    if (reference.TryGetTarget(out var path)) paths.Add(path);
                }
            }

            // each on the thread of its window
            foreach (var path in paths)
            {
                path.DispatcherQueue?.TryEnqueue(() => Apply(path));
            }
        }


        // === attached properties ===

        // on a Path: the sensor whose source it draws; the Path gets a geometry of its own, and collapses without a
        // sensor or while the icon of its source is switched off (the panel around it drops the gap then);
        // Not a binding on Path.Data: x:Bind evaluates one expression once for all its targets, and a geometry that
        // sits in one Path already makes the next Path throw (E_INVALIDARG)
        public static readonly DependencyProperty SensorIdProperty =
            DependencyProperty.RegisterAttached(
                "SensorId",
                typeof(string),
                typeof(SensorSourceIcon),
                new PropertyMetadata(null, OnSensorIdChanged));

        public static string GetSensorId(DependencyObject element) => (string)element.GetValue(SensorIdProperty);

        public static void SetSensorId(DependencyObject element, string value) => element.SetValue(SensorIdProperty, value);

        private static void OnSensorIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Path path) return;

            if (e.OldValue == null) Register(path);
            Apply(path);
        }


        // === public api ===

        // for the tooltip; product names, not translated
        public static string GetName(string sensorId)
        {
            if (string.IsNullOrEmpty(sensorId)) return "";

            return SensorSourceMarker.IsWindowsSensor(sensorId) ? "Windows" : "LibreHardwareMonitor";
        }


        // the same mark as an icon element, for a place that names the source itself (a settings header)
        public static PathIcon CreateIcon(bool isWindows)
        {
            return AppIcons.CreateIcon(isWindows ? AppIconKind.Windows : AppIconKind.Lhm);
        }


        // === private helpers ===

        private static void Apply(Path path)
        {
            string sensorId = GetSensorId(path);
            bool isVisible = !string.IsNullOrEmpty(sensorId)
                && (SensorSourceMarker.IsWindowsSensor(sensorId) ? IsWindowsIconVisible : IsLhmIconVisible);

            path.Data = isVisible ? CreateGeometry(sensorId) : null;
            path.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void Register(Path path)
        {
            lock (_pathsLock)
            {
                _paths.Add(new WeakReference<Path>(path));

                if (++_registrationsSincePrune < PruneEvery) return;
                _registrationsSincePrune = 0;
                _paths.RemoveAll(reference => !reference.TryGetTarget(out _));
            }
        }

        // a new geometry per call
        private static Geometry CreateGeometry(string sensorId) =>
            AppIcons.CreateGeometry(SensorSourceMarker.IsWindowsSensor(sensorId) ? AppIconKind.Windows : AppIconKind.Lhm);
    }
}
