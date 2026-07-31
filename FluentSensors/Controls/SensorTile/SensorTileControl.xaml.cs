using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Controls.SensorTile
{
    // compact "title + current value" tile for a single sensor; the display-only counterpart to
    // SensorGraph/SensorPanelControl (which additionally renders the LiveChart); the two are related but used in
    // different places, so this lives in its own sibling folder rather than inside SensorGraph/
    //
    // Title is a plain fixed string set by the consumer, not derived from ViewModel.SensorName: several LHM sensors
    // share the exact same raw name across different SensorTypes (e.g. "GPU Core" is both a Load and a Clock
    // sensor), so a name pulled straight from the sensor would be ambiguous here
    // This also means the title stays visible even when ViewModel is null (sensor not found on this hardware instance)
    public sealed partial class SensorTileControl : UserControl
    {
        public SensorTileControl()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public SensorGraphViewModel ViewModel
        {
            get => (SensorGraphViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorGraphViewModel),
                typeof(SensorTileControl),
                new PropertyMetadata(null));

        // always shown as-is, regardless of whether ViewModel is set
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(SensorTileControl),
                new PropertyMetadata(string.Empty));

        // whether the small info button next to the title is shown at all
        public bool ShowInfoButton
        {
            get => (bool)GetValue(ShowInfoButtonProperty);
            set => SetValue(ShowInfoButtonProperty, value);
        }
        public static readonly DependencyProperty ShowInfoButtonProperty =
            DependencyProperty.Register(
                nameof(ShowInfoButton),
                typeof(bool),
                typeof(SensorTileControl),
                new PropertyMetadata(false));

        // text shown in the flyout when the info button is tapped; no effect if ShowInfoButton is false
        public string InfoMessage
        {
            get => (string)GetValue(InfoMessageProperty);
            set => SetValue(InfoMessageProperty, value);
        }
        public static readonly DependencyProperty InfoMessageProperty =
            DependencyProperty.Register(
                nameof(InfoMessage),
                typeof(string),
                typeof(SensorTileControl),
                new PropertyMetadata(string.Empty));


        // === bindable helper surfaces ===

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // these two only need to react to ViewModel itself changing (a real object <-> null), which x:Bind
        // tracks correctly for function bindings
        // unlike CurrentValueText/CurrentValueColor, which live inside an otherwise-unchanging ViewModel instance
        // and must be bound as direct property paths instead (see the two TextBlocks in the XAML) for their live
        // PropertyChanged ticks to actually be picked up
        private Visibility GetValueVisibility(SensorGraphViewModel viewModel) =>
            viewModel != null ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetNotFoundVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Visible : Visibility.Collapsed;
    }
}