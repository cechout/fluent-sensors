namespace FluentSensors.Controls.SensorGraph
{
    // what a tap gesture (on the graph itself, or on the toggle button in the status row) should trigger
    public enum TapAction
    {
        // tap does nothing
        None,

        // opens/closes the button-based control panel (Y-axis + threshold arrow buttons)
        TogglePanel,

        // opens the compact threshold editor flyout instead of the button-based control panel
        ShowFlyout
    }
}