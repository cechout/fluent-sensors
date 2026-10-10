using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;


namespace FluentSensors.Common.UI
{
    // the acrylic values of the four materials, shared by the widget, the csv logger and the taskbar flyout
    public static class BackdropMaterials
    {
        // --- system acrylic preset (BackdropMaterial.SystemAcrylic with Windows transparency on) ---
        // over a flat backdrop the controller resolves to lerp(backdrop, tint, luminosity); the native shell flyouts
        // measure as 4 percent backdrop transmission in dark and 9.4 in light (their bottom bar, the bare material)
        // TintOpacity: the tint blend carries hue and saturation only, so a gray tint leaves every gray value alone
        // and only takes the color out of what shows through; the native flyouts pass far less color than the
        // default does (the source names its BlendEffectMode swapped, the tint layer reads as Luminosity there):
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.cpp
        // tints are the Fluent acrylic base tones, AcrylicBackgroundFillColorBaseBrush, not pre-compensated (the
        // render shift applies once to the finished composite):
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush_19h1_themeresources.xaml
        public static readonly Color SystemAcrylicDarkTintColor = Color.FromArgb(255, 0x20, 0x20, 0x20);
        public const float SystemAcrylicDarkLuminosity = 0.96f;
        public const float SystemAcrylicDarkTintOpacity = 0.8f;

        public static readonly Color SystemAcrylicLightTintColor = Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
        public const float SystemAcrylicLightLuminosity = 0.902f;
        public const float SystemAcrylicLightTintOpacity = 0.5f;


        // === public methods ===

        public static bool IsAcrylic(BackdropMaterial material) =>
            material is BackdropMaterial.SystemAcrylic or BackdropMaterial.CustomAcrylic;

        // the live accent or the custom color; the tint of custom acrylic and the fill of solid
        public static Color ResolveTintColor(bool useAccentColor, Color customColor) =>
            useAccentColor ? (Color)Application.Current.Resources["SystemAccentColor"] : customColor;

        // system acrylic takes the preset of the theme, custom acrylic the users values; the fallback (what shows with
        // Windows transparency off) is the preset tint, which is the plain window color, or the users tint
        public static void Configure(
            DesktopAcrylicController controller, BackdropMaterial material, bool isLight,
            Color tintColor, float tintOpacity, float luminosityOpacity)
        {
            if (material == BackdropMaterial.SystemAcrylic)
            {
                Color presetTint = isLight ? SystemAcrylicLightTintColor : SystemAcrylicDarkTintColor;
                controller.TintColor = presetTint;
                controller.TintOpacity = isLight ? SystemAcrylicLightTintOpacity : SystemAcrylicDarkTintOpacity;
                controller.LuminosityOpacity = isLight ? SystemAcrylicLightLuminosity : SystemAcrylicDarkLuminosity;
                controller.FallbackColor = presetTint;
            }
            else
            {
                controller.TintColor = tintColor;
                controller.TintOpacity = tintOpacity;
                controller.LuminosityOpacity = luminosityOpacity;
                controller.FallbackColor = tintColor;
            }
        }
    }
}
