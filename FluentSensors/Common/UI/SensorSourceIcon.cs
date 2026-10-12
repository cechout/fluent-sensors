using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Common.UI
{
    // the source icon:
    // a small mark behind a sensor title that says who measured the value, LibreHardwareMonitor or Windows
    // vector data for a Path, so it takes the text colour of its place, follows the theme and stays sharp at any scale;
    // neither system icon font has a glyph for either
    public static class SensorSourceIcon
    {
        // both drawn on a 16 x 16 grid and filling 1 to 15, so they come out the same size

        // four panes, their corners rounded by about half a pixel at the drawn size
        private const string WindowsData =
            "M1.7,1H6.8A0.7,0.7 0 0 1 7.5,1.7V6.8A0.7,0.7 0 0 1 6.8,7.5H1.7A0.7,0.7 0 0 1 1,6.8V1.7A0.7,0.7 0 0 1 1.7,1Z " +
            "M9.2,1H14.3A0.7,0.7 0 0 1 15,1.7V6.8A0.7,0.7 0 0 1 14.3,7.5H9.2A0.7,0.7 0 0 1 8.5,6.8V1.7A0.7,0.7 0 0 1 9.2,1Z " +
            "M1.7,8.5H6.8A0.7,0.7 0 0 1 7.5,9.2V14.3A0.7,0.7 0 0 1 6.8,15H1.7A0.7,0.7 0 0 1 1,14.3V9.2A0.7,0.7 0 0 1 1.7,8.5Z " +
            "M9.2,8.5H14.3A0.7,0.7 0 0 1 15,9.2V14.3A0.7,0.7 0 0 1 14.3,15H9.2A0.7,0.7 0 0 1 8.5,14.3V9.2A0.7,0.7 0 0 1 9.2,8.5Z";

        // a chip seen from above: the package as a frame, the die in its middle, the pin 1 corner bottom left and two
        // contacts on the right; even-odd, so the inner square of the frame is a hole the other parts sit in
        private const string LhmData =
            "F0 M2.6,1H13.4A1.6,1.6 0 0 1 15,2.6V13.4A1.6,1.6 0 0 1 13.4,15H2.6A1.6,1.6 0 0 1 1,13.4V2.6A1.6,1.6 0 0 1 2.6,1Z " +
            "M3.1,2.5H12.9A0.6,0.6 0 0 1 13.5,3.1V12.9A0.6,0.6 0 0 1 12.9,13.5H3.1A0.6,0.6 0 0 1 2.5,12.9V3.1A0.6,0.6 0 0 1 3.1,2.5Z " +
            "M6.1,5.5H9.9A0.6,0.6 0 0 1 10.5,6.1V9.9A0.6,0.6 0 0 1 9.9,10.5H6.1A0.6,0.6 0 0 1 5.5,9.9V6.1A0.6,0.6 0 0 1 6.1,5.5Z " +
            "M3.5,12.5V9.5L6.5,12.5Z " +
            "M11.35,3.5H12.15A0.35,0.35 0 0 1 12.5,3.85V4.65A0.35,0.35 0 0 1 12.15,5H11.35A0.35,0.35 0 0 1 11,4.65V3.85A0.35,0.35 0 0 1 11.35,3.5Z " +
            "M11.35,11H12.15A0.35,0.35 0 0 1 12.5,11.35V12.15A0.35,0.35 0 0 1 12.15,12.5H11.35A0.35,0.35 0 0 1 11,12.15V11.35A0.35,0.35 0 0 1 11.35,11Z";


        // === attached properties ===

        // on a Path: the sensor whose source it draws; the Path gets a geometry of its own
        // not a binding on Path.Data: x:Bind evaluates one expression once for all its targets, and a geometry that
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
            if (d is Path path) path.Data = CreateGeometry(e.NewValue as string);
        }


        // === public api ===

        // for the tooltip; product names, not translated
        public static string GetName(string sensorId)
        {
            if (string.IsNullOrEmpty(sensorId)) return "";

            return SensorSourceMarker.IsWindowsSensor(sensorId) ? "Windows" : "LibreHardwareMonitor";
        }


        // === private helpers ===

        // a new geometry per call; null without a sensor, the Path then draws nothing
        private static Geometry CreateGeometry(string sensorId)
        {
            if (string.IsNullOrEmpty(sensorId)) return null;

            string data = SensorSourceMarker.IsWindowsSensor(sensorId) ? WindowsData : LhmData;
            return (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
        }
    }
}
