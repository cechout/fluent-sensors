namespace FluentSensors.Common.UI
{
    // the background material of a window group; the widget and csv windows share one setting, the taskbar flyout
    // has its own
    public enum BackdropMaterial
    {
        Mica, // the wallpaper tinted; with Windows transparency off a plain window
        SystemAcrylic, // the blur with the preset in BackdropMaterials; with transparency off a plain window
        CustomAcrylic, // the blur with the users tint, tint opacity and luminosity; flat tint color without transparency
        Solid // the accent or the custom color
    }
}
