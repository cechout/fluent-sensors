using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.TimeRange
{
    // the time range picker:
    // the graph time range as a subtle button with a chevron; a click opens the dropdown of Options, a pick writes
    // SelectedSeconds, which the consumer binds TwoWay to its setting
    // the consumer sets Height and Padding; (18 and 4,0,4,0 by default, the status row of SensorPanelControl and its
    // switch button)
    public sealed partial class TimeRangePickerControl : UserControl
    {
        // === constructor ===

        public TimeRangePickerControl()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        // the offered time ranges, one of the GraphTimeRanges lists
        public IReadOnlyList<GraphTimeRange> Options
        {
            get => (IReadOnlyList<GraphTimeRange>)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }
        public static readonly DependencyProperty OptionsProperty =
            DependencyProperty.Register(
                nameof(Options),
                typeof(IReadOnlyList<GraphTimeRange>),
                typeof(TimeRangePickerControl),
                new PropertyMetadata(null, OnOptionsChanged));

        // set from code, so the selection follows at once; (x:Bind would only fill ItemsSource on Loading)
        private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TimeRangePickerControl picker) return;

            picker.OptionsComboBox.ItemsSource = e.NewValue;
            picker.SyncSelection();
        }

        public double SelectedSeconds
        {
            get => (double)GetValue(SelectedSecondsProperty);
            set => SetValue(SelectedSecondsProperty, value);
        }
        public static readonly DependencyProperty SelectedSecondsProperty =
            DependencyProperty.Register(
                nameof(SelectedSeconds),
                typeof(double),
                typeof(TimeRangePickerControl),
                new PropertyMetadata(0.0, OnSelectedSecondsChanged));

        // a prefix for the button text; empty reads "Last 60s", "Flyout" reads "Flyout 60s"
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(TimeRangePickerControl),
                new PropertyMetadata(string.Empty));

        // the name of button and combobox for a screen reader
        public string AccessibleName
        {
            get => (string)GetValue(AccessibleNameProperty);
            set => SetValue(AccessibleNameProperty, value);
        }
        public static readonly DependencyProperty AccessibleNameProperty =
            DependencyProperty.Register(
                nameof(AccessibleName),
                typeof(string),
                typeof(TimeRangePickerControl),
                new PropertyMetadata(AppStrings.Get("TimeRange_Name")));

        // only set when a consumer supplies one, so the button style keeps its theme-reactive default
        public Brush TextForeground
        {
            get => (Brush)GetValue(TextForegroundProperty);
            set => SetValue(TextForegroundProperty, value);
        }
        public static readonly DependencyProperty TextForegroundProperty =
            DependencyProperty.Register(
                nameof(TextForeground),
                typeof(Brush),
                typeof(TimeRangePickerControl),
                new PropertyMetadata(null, OnTextForegroundChanged));

        private static void OnTextForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TimeRangePickerControl picker || e.NewValue is not Brush brush) return;

            picker.PickerButton.Foreground = brush;
        }

        private static void OnSelectedSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimeRangePickerControl picker) picker.SyncSelection();
        }


        // === binding helpers ===

        private string FormatLabel(string label, double seconds)
        {
            return string.IsNullOrEmpty(label) ? AppStrings.Format("TimeRange_Last", seconds) : $"{label} {seconds:0}s";
        }


        // === event handlers ===

        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            OptionsComboBox.IsDropDownOpen = true;
        }

        // the dropdown opens over the combobox and is at least as wide, so it takes the size of the button
        private void PickerButton_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            OptionsComboBox.Width = e.NewSize.Width;
            OptionsComboBox.Height = e.NewSize.Height;
        }

        // keeps the closed combobox on the current value
        private void OptionsComboBox_DropDownOpened(object sender, object e)
        {
            SyncSelection();
        }

        // the closing combobox takes the focus, invisible but still under the arrow keys; queued after that, the focus
        // goes back to the button
        private void OptionsComboBox_DropDownClosed(object sender, object e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (OptionsComboBox.FocusState != FocusState.Unfocused) PickerButton.Focus(FocusState.Programmatic);
            });
        }

        private void OptionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OptionsComboBox.SelectedItem is not GraphTimeRange option) return;
            if (option.Seconds == SelectedSeconds) return;

            SelectedSeconds = option.Seconds;
        }


        // === private helpers ===

        // a value missing from Options leaves the combobox without a selection; the button still shows it
        private void SyncSelection()
        {
            OptionsComboBox.SelectedItem = GraphTimeRanges.Find(Options, SelectedSeconds);
        }
    }
}
