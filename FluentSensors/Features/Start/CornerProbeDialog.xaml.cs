using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Features.Start
{
    // a ContentDialog that does nothing except be declared in XAML, used to tell apart a dialog built in code
    // from one loaded out of compiled markup while the missing corner radius is being tracked down
    //
    // this one is square while the identical dialog built in code is round, so it reads back the values that
    // decide the rounding and puts them on screen: a style setter lands between construction and the template
    // being applied, a local value does not, and the difference says which of the two is missing
    public sealed partial class CornerProbeDialog : ContentDialog
    {
        public CornerProbeDialog()
        {
            this.InitializeComponent();

            string atBuild = Describe("after InitializeComponent");
            this.Opened += (_, _) => ProbeText.Text = atBuild + "\n\n" + Describe("after the template was applied");
        }

        private string Describe(string when)
        {
            string overlay = Application.Current.Resources.TryGetValue("OverlayCornerRadius", out object value)
                ? value.ToString()
                : "not found";

            return $"{when}\n" +
                   $"this.CornerRadius = {this.CornerRadius}\n" +
                   $"this.Background = {(this.Background == null ? "null" : "set")}\n" +
                   $"this.BorderThickness = {this.BorderThickness}\n" +
                   $"app OverlayCornerRadius = {overlay}";
        }
    }
}
