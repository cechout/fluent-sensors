using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // the side taskbar slot:
    // one pinned sensor on a left or right taskbar:
    // - the name on top, value and unit at the bottom; or both stacked at the top, in the middle or at the bottom
    // - on the left or the right, in the size and weight of TextLayout
    // - the graph behind, in the picked direction; or a compact card with its level bar across the foot, or below
    //   the centered text
    // (the text never turns)
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

        // the taskbar text settings (SensorGraphViewModel.LabelLayout); a side slot reads size, weight and side
        public GraphLabelLayout TextLayout
        {
            get => (GraphLabelLayout)GetValue(TextLayoutProperty);
            set => SetValue(TextLayoutProperty, value);
        }

        public static readonly DependencyProperty TextLayoutProperty =
            DependencyProperty.Register(
                nameof(TextLayout),
                typeof(GraphLabelLayout),
                typeof(TaskbarSideSlot),
                new PropertyMetadata(GraphLabelLayout.Default, (d, e) => ((TaskbarSideSlot)d).ApplyTextLayout()));

        // a compact card (SensorGraphViewModel.IsCompactCard): never turned, the text moves up off its level bar
        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(TaskbarSideSlot),
                new PropertyMetadata(false, (d, e) => ((TaskbarSideSlot)d).ApplyGraphTurn()));

        private bool IsTextCentered => (TextLayout ?? GraphLabelLayout.Default).SidePosition == SideLabelPosition.Center;

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

        // the room the level bar of a compact card takes at the foot: its inset and the bar, and a 2 DIP gap
        private const double CompactLevelBarRoomDip = SensorGraphControl.LevelBarFootInset + SensorGraphControl.LevelBarThickness + 2;

        // the room the level bar of a compact card takes below centered text: the bar and its gap
        private const double CompactCenteredLevelBarRoomDip = SensorGraphControl.LevelBarThickness + SensorGraphControl.LevelBarCenterGap;

        // the last layout from ApplyLayout, for a compact switch in between
        private ScreenEdge _edge = ScreenEdge.Left;
        private string _graphDirection = "RightToLeft";


        // === constructor ===

        public TaskbarSideSlot()
        {
            this.InitializeComponent();
            this.SizeChanged += (s, e) => SlotWidth = e.NewSize.Width;
        }


        // === public methods ===

        // turns the graph so its values run in the picked direction, with the graph baseline on the screen edge side:
        // - RightToLeft: not turned, the same graph as on a horizontal taskbar
        // - TopToBottom: counterclockwise, newest value at the top, baseline on the right
        // - BottomToTop: clockwise, newest value at the bottom, baseline on the left
        // (the mirror moves the baseline over wherever the turn left it on the desktop side)
        public void ApplyLayout(ScreenEdge edge, string graphDirection, int nameLines)
        {
            _edge = edge;
            _graphDirection = graphDirection;
            ApplyGraphTurn();

            bool twoLines = nameLines == 2;
            NameText.MaxLines = twoLines ? 2 : 1;
            NameText.TextWrapping = twoLines ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }

        private void ApplyGraphTurn()
        {
            switch (IsCompact ? "RightToLeft" : _graphDirection)
            {
                case "TopToBottom":
                    GraphRotation.QuarterTurns = 3;
                    GraphRotation.IsMirrored = _edge == ScreenEdge.Left;
                    break;

                case "BottomToTop":
                    GraphRotation.QuarterTurns = 1;
                    GraphRotation.IsMirrored = _edge == ScreenEdge.Right;
                    break;

                default:
                    GraphRotation.QuarterTurns = 0;
                    GraphRotation.IsMirrored = false;
                    break;
            }

            ApplyTextPlacement();
        }

        // where the text block sits:
        // - spread or at one end: the full slot, a compact card keeps its foot free for the level bar
        // - centered: on the graph card, a compact card centers it together with the level bar below
        private void ApplyTextPlacement()
        {
            // the name margin counts from the top of the graph card: an unturned card starts PanelLabelRowGapDip below
            // the slot top, a turned one has that gap on its side and fills the slot along the taskbar
            double cardTop = GraphRotation.QuarterTurns == 0 ? PanelLabelRowGapDip : 0;

            if (IsTextCentered)
            {
                NameHost.Padding = new Thickness(0);
                TextHost.VerticalAlignment = VerticalAlignment.Center;
                double lift = 2 * SensorGraphControl.CenteredTextLift; // the bar of a compact card moves up with it
                TextHost.Margin = new Thickness(0, cardTop, 0, (IsCompact ? CompactCenteredLevelBarRoomDip : 0) + lift);
            }
            else
            {
                NameHost.Padding = new Thickness(0, cardTop, 0, 0);
                TextHost.VerticalAlignment = VerticalAlignment.Stretch;
                TextHost.Margin = new Thickness(0, 0, 0, IsCompact ? CompactLevelBarRoomDip : 0);
            }

            UpdateLevelBarTextHeight();
        }

        // the graph card puts its level bar below centered text by its height
        private void UpdateLevelBarTextHeight()
        {
            Panel.LevelBarTextHeight = IsCompact && IsTextCentered ? TextHost.ActualHeight : double.NaN;
        }

        private void TextHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLevelBarTextHeight();


        // === text layout ===

        // size, weight and side of name and value; the line heights keep the gaps of the 10 DIP layout:
        // - name: one more than the size, the gap between its two lines
        // - value: three more, its line box against the bottom
        private void ApplyTextLayout()
        {
            var layout = TextLayout ?? GraphLabelLayout.Default;
            var side = layout.IsRightAligned ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            NameText.FontSize = layout.FontSize;
            NameText.LineHeight = layout.FontSize + 1;
            NameRow.HorizontalAlignment = side;
            SourceIcon.Margin = new Thickness(0, 5 + (layout.FontSize - GraphLabelLayout.Default.FontSize) / 2, 4, 0); // on the first line

            ValueText.FontSize = layout.FontSize;
            ValueText.LineHeight = layout.FontSize + 3;
            ValueText.FontWeight = layout.IsValueSemiBold ? FontWeights.SemiBold : FontWeights.Normal;
            ValueText.HorizontalAlignment = side;

            // - spread: name in the top row, value in the bottom row
            // - stacked: both next to each other at one end
            // - centered: as spread, in a text block that sizes to them (ApplyTextPlacement), so the gap row is empty
            (int nameRow, int valueRow) = layout.SidePosition switch
            {
                SideLabelPosition.Top => (0, 1),
                SideLabelPosition.Bottom => (1, 2),
                _ => (0, 2)
            };
            Grid.SetRow(NameHost, nameRow);
            Grid.SetRow(ValueText, valueRow);
            NameHost.VerticalAlignment = nameRow == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            ValueText.VerticalAlignment = valueRow == 1 ? VerticalAlignment.Top : VerticalAlignment.Bottom;

            ApplyTextPlacement();

            _measureText = null; // measures with the new font next time
        }


        // === value text ===

        // value and unit when both fit, else the bare value; a cut unit reads worse than none, and
        // it never changes per sensor
        // the unit is everything after the space SensorUnitFormatter puts before it
        // (textLayout only reruns this after a size or weight change)
        private string FitValueText(string? text, double slotWidth, GraphLabelLayout textLayout)
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
