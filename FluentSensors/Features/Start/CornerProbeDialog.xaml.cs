using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Features.Start
{
    // a ContentDialog that does nothing except be declared in XAML, used to tell apart a dialog built in code
    // from one loaded out of compiled markup while the missing corner radius is being tracked down
    public sealed partial class CornerProbeDialog : ContentDialog
    {
        public CornerProbeDialog()
        {
            this.InitializeComponent();
        }
    }
}
