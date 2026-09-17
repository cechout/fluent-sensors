using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text;


namespace FluentSensors.Features.Start
{
    // a ContentDialog that does nothing except be declared in XAML, used to tell apart a dialog built in code
    // from one loaded out of compiled markup while the missing corner radius is being tracked down
    //
    // this one is square while the identical dialog built in code is round, so it reads back the values the
    // rounding depends on, at both points where they can change, and writes them next to the exe
    // ReadLocalValue is the instrument that settles it: Unset means nothing was assigned directly, in which case
    // whatever the property reports has to have come from the default style, or from nowhere at all
    public sealed partial class CornerProbeDialog : ContentDialog
    {
        // read by whoever is debugging this; sits next to the exe so it is easy to find and easy to delete
        public static string DumpPath => Path.Combine(AppContext.BaseDirectory, "corner-probe.txt");

        public CornerProbeDialog()
        {
            this.InitializeComponent();

            var report = new StringBuilder();
            report.AppendLine(Describe(this, "xaml dialog, after InitializeComponent"));
            report.AppendLine(Describe(new ContentDialog(), "code built dialog, after the constructor"));

            this.Opened += (_, _) =>
            {
                report.AppendLine(Describe(this, "xaml dialog, after the template was applied"));

                string text = report.ToString();
                ProbeText.Text = text;

                try { File.WriteAllText(DumpPath, text); }
                catch { /* the readout on screen is the fallback */ }
            };
        }

        private static string Describe(ContentDialog dialog, string when)
        {
            string overlay = Application.Current.Resources.TryGetValue("OverlayCornerRadius", out object value)
                ? value.ToString()
                : "not found";

            return $"--- {when} ---\n" +
                   $"type                 {dialog.GetType().Name}\n" +
                   $"Style                {(dialog.Style == null ? "null" : "set")}\n" +
                   $"CornerRadius         {dialog.CornerRadius}\n" +
                   $"CornerRadius local   {Local(dialog, CornerRadiusProperty)}\n" +
                   $"Background           {(dialog.Background == null ? "null" : "set")}\n" +
                   $"Background local     {Local(dialog, BackgroundProperty)}\n" +
                   $"BorderThickness      {dialog.BorderThickness}\n" +
                   $"app OverlayCornerRadius  {overlay}\n";
        }

        private static string Local(DependencyObject dialog, DependencyProperty property)
        {
            object value = dialog.ReadLocalValue(property);
            return value == DependencyProperty.UnsetValue ? "unset" : value.ToString();
        }
    }
}
