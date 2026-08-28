namespace FluentSensors.Features.TaskbarWidget
{
    // which end of the taskbar the widget anchors to
    // Center deliberately excluded: on a centered taskbar the app icons grow and shrink with every
    // open window, an anchor there would overlap constantly
    public enum TaskbarAnchor
    {
        Start,
        End
    }
}
