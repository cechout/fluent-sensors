using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Controls.SensorTile
{
    // the sensor tile:
    // title and current value of one sensor, the chartless counterpart of SensorPanelControl
    // Title comes from the consumer, since one LHM name spans several types ("GPU Core" is Load and Clock); a slot may
    // bind it to ViewModel.SensorName, and it shows without a ViewModel too
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

        // shown as is, with or without ViewModel
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

        // the info button next to the title
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

        // the popup text of the info button
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

        // invisible behind the value, reserves the width of the longest one ("100 %", "999 MHz") so an
        // auto width tile holds still
        public string MaxValueText
        {
            get => (string)GetValue(MaxValueTextProperty);
            set => SetValue(MaxValueTextProperty, value);
        }
        public static readonly DependencyProperty MaxValueTextProperty =
            DependencyProperty.Register(
                nameof(MaxValueText),
                typeof(string),
                typeof(SensorTileControl),
                new PropertyMetadata(string.Empty));


        // === bindable helper surfaces ===

        // these follow ViewModel itself (object or null), which function bindings track; CurrentValueText and
        // CurrentValueColor change inside it and are bound as property paths in the XAML
        private Visibility GetValueVisibility(SensorGraphViewModel viewModel) =>
            viewModel != null ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetNotFoundVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
