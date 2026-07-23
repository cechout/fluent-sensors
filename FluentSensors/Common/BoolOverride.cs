namespace FluentSensors.Common
{
    // 3-state alternative to bool? for DependencyProperty use
    // nullable value types as DependencyProperty types have a history of XAML attribute-parsing issues in WinUI,
    // this sidesteps that entirely
    public enum BoolOverride
    {
        Inherit,
        True,
        False
    }
}