namespace FluentSensors.Controls.SensorGraph
{
    // what a tap on the graph or on the status row toggle button does
    public enum TapAction
    {
        None,

        // the button panel (y-axis and threshold arrows)
        TogglePanel,

        // the threshold editor flyout instead
        ShowFlyout
    }
}
