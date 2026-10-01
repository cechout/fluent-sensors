using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // one pinned sensor on a taskbar docked to the left or right screen edge: the name on top, value and unit at the
    // bottom left, the graph behind both; the graph runs in the direction picked in the settings, the text never turns
    //
    // TaskbarWidgetWindow knows the taskbar edge and pushes the layout through ApplyLayout, when a slot loads and on
    // every change after that
    public sealed partial class TaskbarSideSlot : UserControl
    {
        public SensorGraphViewModel? ViewModel
        {
            get => (SensorGraphViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorGraphViewModel),
                typeof(TaskbarSideSlot),
                new PropertyMetadata(null));

        // width of the slot, kept as a dependency property so FitValueText runs again when it changes
        private double SlotWidth
        {
            get => (double)GetValue(SlotWidthProperty);
            set => SetValue(SlotWidthProperty, value);
        }

        private static readonly DependencyProperty SlotWidthProperty =
            DependencyProperty.Register(
                nameof(SlotWidth),
                typeof(double),
                typeof(TaskbarSideSlot),
                new PropertyMetadata(0.0));

        // off-tree copy of ValueText, used only to measure a candidate text
        private TextBlock? _measureText;


        // === constructor ===

        public TaskbarSideSlot()
        {
            this.InitializeComponent();
            this.SizeChanged += (s, e) => SlotWidth = e.NewSize.Width;
        }


        // === public methods ===

        public static bool TurnsGraph(string graphDirection) => graphDirection is "TopToBottom" or "BottomToTop";

        // turns the graph so its values run in the picked direction, with the graph baseline on the screen edge side:
        // RightToLeft - not turned, the same graph as on a horizontal taskbar
        // TopToBottom - counterclockwise, newest value at the top, baseline on the right
        // BottomToTop - clockwise, newest value at the bottom, baseline on the left
        // the mirror moves the baseline over wherever the turn left it on the desktop side
        public void ApplyLayout(ScreenEdge edge, string graphDirection, int nameLines)
        {
            switch (graphDirection)
            {
                case "TopToBottom":
                    GraphRotation.QuarterTurns = 3;
                    GraphRotation.IsMirrored = edge == ScreenEdge.Left;
                    break;

                case "BottomToTop":
                    GraphRotation.QuarterTurns = 1;
                    GraphRotation.IsMirrored = edge == ScreenEdge.Right;
                    break;

                default:
                    GraphRotation.QuarterTurns = 0;
                    GraphRotation.IsMirrored = false;
                    break;
            }

            bool twoLines = nameLines == 2;
            NameText.MaxLines = twoLines ? 2 : 1;
            NameText.TextWrapping = twoLines ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }


        // === value text ===

        // value and unit when both fit the slot width, the bare value when they do not; a cut or wrapped unit reads
        // worse than none, and the unit never changes between readings of the same sensor
        // the unit is everything after the space SensorUnitFormatter puts between number and unit
        private string FitValueText(string? text, double slotWidth)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int unitStart = text.IndexOf(' ');
            if (unitStart <= 0) return text;

            double availableWidth = slotWidth - ValueText.Margin.Left - ValueText.Margin.Right;
            return MeasureTextWidth(text) <= availableWidth ? text : text.Substring(0, unitStart);
        }

        private double MeasureTextWidth(string text)
        {
            if (_measureText == null)
            {
                _measureText = new TextBlock
                {
                    FontFamily = ValueText.FontFamily,
                    FontSize = ValueText.FontSize,
                    FontWeight = ValueText.FontWeight
                };
                Typography.SetNumeralAlignment(_measureText, FontNumeralAlignment.Tabular);
            }

            _measureText.Text = text;
            _measureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _measureText.DesiredSize.Width;
        }
    }
}
