using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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


        // === constructor ===

        public TaskbarSideSlot()
        {
            this.InitializeComponent();
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

            bool twoLines = nameLines == 2;
            NameText.MaxLines = twoLines ? 2 : 1;
            NameText.TextWrapping = twoLines ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
    }
}
