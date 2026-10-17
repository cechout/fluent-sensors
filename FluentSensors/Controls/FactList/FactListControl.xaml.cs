using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;



namespace FluentSensors.Controls.FactList
{
    // a wrapped list of info panel facts:
    // for the facts a view model builds as a list (a set that depends on the hardware, or LHM sensors read as
    // facts), so the panel needs no markup per fact
    public sealed partial class FactListControl : UserControl
    {
        // === constructor ===

        public FactListControl()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public object Facts
        {
            get => GetValue(FactsProperty);
            set => SetValue(FactsProperty, value);
        }

        public static readonly DependencyProperty FactsProperty =
            DependencyProperty.Register(
                nameof(Facts),
                typeof(object),
                typeof(FactListControl),
                new PropertyMetadata(null));
    }
}
