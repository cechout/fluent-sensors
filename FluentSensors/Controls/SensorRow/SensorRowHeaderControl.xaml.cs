using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace FluentSensors.Controls.SensorRow
{
    // the column labels above a list of SensorRowControls, with the same columns and the same compact switch as the
    // row itself, so the sensors page and the hidden sensors window do not each carry a copy of the column layout
    public sealed partial class SensorRowHeaderControl : UserControl
    {
        // === constructor ===

        public SensorRowHeaderControl()
        {
            this.InitializeComponent();
        }


        // === dependency properties ===

        // true above the rows in the hidden sensors window, where SensorRowControl.IsCompact is set as well
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(SensorRowHeaderControl),
                new PropertyMetadata(false, OnIsCompactChanged));

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorRowHeaderControl header) header.UpdateColumns();
        }


        // === private helpers ===

        // the same name-only collapse SensorRowControl.UpdateDisplayState does, label for column
        private void UpdateColumns()
        {
            if (!IsCompact) return;

            ThresholdLabel.Visibility = Visibility.Collapsed;
            CurrentLabel.Visibility = Visibility.Collapsed;
            MinimumLabel.Visibility = Visibility.Collapsed;
            MaximumLabel.Visibility = Visibility.Collapsed;
            AverageLabel.Visibility = Visibility.Collapsed;

            CurrentColumn.MinWidth = 0;
            ThresholdColumn.MinWidth = 0;
            MinimumColumn.MinWidth = 0;
            MaximumColumn.MinWidth = 0;
            AverageColumn.MinWidth = 0;

            CurrentColumn.Width = new GridLength(0);
            ThresholdColumn.Width = new GridLength(0);
            MinimumColumn.Width = new GridLength(0);
            MaximumColumn.Width = new GridLength(0);
            AverageColumn.Width = new GridLength(0);

            NameColumn.Width = new GridLength(3, GridUnitType.Star);
            UnitColumn.Width = new GridLength(40);
            UnitLabel.Visibility = Visibility.Visible;
        }
    }
}
