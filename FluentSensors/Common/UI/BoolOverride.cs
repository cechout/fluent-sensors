namespace FluentSensors.Common.UI
{
    // a three-state bool? for dependency properties, where nullable value types parse badly from XAML in WinUI
    public enum BoolOverride
    {
        Inherit,
        True,
        False
    }
}
