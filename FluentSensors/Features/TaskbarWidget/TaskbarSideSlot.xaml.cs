using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // the side taskbar slot:
    // one pinned sensor on a left or right taskbar, the name on top, value and unit bottom left, the graph behind in
    // the picked direction; the text never turns
    // TaskbarWidgetWindow pushes the layout through ApplyLayout on load and on every change
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

        // a dependency property, so FitValueText reruns on a change
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

        // SensorPanelControl keeps this gap above its graph card for the label row it hides here (its RowSpacing)
        private const double PanelLabelRowGapDip = 3;


        // === constructor ===

        public TaskbarSideSlot()
        {
            this.InitializeComponent();
            this.SizeChanged += (s, e) => SlotWidth = e.NewSize.Width;
        }


        // === public methods ===

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

            // the name margin counts from the top of the graph card: an unturned card starts PanelLabelRowGapDip below
            // the slot top, a turned one has that gap on its side and fills the slot along the taskbar
            NameHost.Padding = new Thickness(0, GraphRotation.QuarterTurns == 0 ? PanelLabelRowGapDip : 0, 0, 0);

            bool twoLines = nameLines == 2;
            NameText.MaxLines = twoLines ? 2 : 1;
            NameText.TextWrapping = twoLines ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }


        // === value text ===

        // value and unit when both fit, else the bare value; a cut unit reads worse than none, and
        // it never changes per sensor
        // the unit is everything after the space SensorUnitFormatter puts before it
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
