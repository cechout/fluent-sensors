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

        // four panes
        private const string WindowsData =
            "M1,1H7.5V7.5H1Z M8.5,1H15V7.5H8.5Z M1,8.5H7.5V15H1Z M8.5,8.5H15V15H8.5Z";

        // a chip seen from above: the package as a frame, the die in its middle, the pin 1 corner bottom left and two
        // contacts on the right; even-odd, so the inner square of the frame is a hole the other parts sit in
        private const string LhmData =
            "F0 M2,1H14A1,1 0 0 1 15,2V14A1,1 0 0 1 14,15H2A1,1 0 0 1 1,14V2A1,1 0 0 1 2,1Z " +
            "M2.5,2.5V13.5H13.5V2.5Z " +
            "M5.5,5.5H10.5V10.5H5.5Z " +
            "M3.5,12.5V9.5L6.5,12.5Z " +
            "M11,3.5H12.5V5H11Z " +
            "M11,11H12.5V12.5H11Z";


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
