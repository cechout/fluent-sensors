using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Common.UI.Icons
{
    // the icons neither system icon font has:
    // vector data for a Path or a PathIcon, so they take the text colour of their place, follow the theme and stay
    // sharp at any scale; every icon of the app that is not a font glyph lives here
    public static class AppIcons
    {
        // all drawn by hand on a 16 x 16 grid and filling 1 to 15, so they come out the same size; the stroke follows the
        // place: one pixel on whole pixels for the small marks beside a title and in a settings header, heavier for a
        // menu, where the glyphs around it are drawn bolder
        // the path syntax (M, H, V, A, Z, F0 for even-odd):
        // https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/move-draw-commands-syntax
        // the same commands in SVG, with pictures; the easiest way into the arc:
        // https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorials/SVG_from_scratch/Paths
        private const double GridSize = 16;

        // windows icon
        // four panes; only the four outer corners of the logo are rounded, by about half a pixel at the drawn size
        private const string WindowsData =
            "M1.7,1H7.5V7.5H1V1.7A0.7,0.7 0 0 1 1.7,1Z " +
            "M8.5,1H14.3A0.7,0.7 0 0 1 15,1.7V7.5H8.5Z " +
            "M1,8.5H7.5V15H1.7A0.7,0.7 0 0 1 1,14.3Z " +
            "M8.5,8.5H15V14.3A0.7,0.7 0 0 1 14.3,15H8.5Z";

        // lhm icon
        // a chip seen from above:
        // - the package as a frame
        // - the die in its middle
        // - the pin 1 corner bottom left
        // - two contacts on the right
        // even-odd, so the inner square of the frame is a hole the other parts sit in:
        // https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Attribute/fill-rule
        private const string LhmData =
            "F0 M2.6,1H13.4A1.6,1.6 0 0 1 15,2.6V13.4A1.6,1.6 0 0 1 13.4,15H2.6A1.6,1.6 0 0 1 1,13.4V2.6A1.6,1.6 0 0 1 2.6,1Z " +
            "M3.1,2.5H12.9A0.6,0.6 0 0 1 13.5,3.1V12.9A0.6,0.6 0 0 1 12.9,13.5H3.1A0.6,0.6 0 0 1 2.5,12.9V3.1A0.6,0.6 0 0 1 3.1,2.5Z " +
            "M6.1,5.5H9.9A0.6,0.6 0 0 1 10.5,6.1V9.9A0.6,0.6 0 0 1 9.9,10.5H6.1A0.6,0.6 0 0 1 5.5,9.9V6.1A0.6,0.6 0 0 1 6.1,5.5Z " +
            "M3.5,12.5V9.5L6.5,12.5Z " +
            "M11.35,3.5H12.15A0.35,0.35 0 0 1 12.5,3.85V4.65A0.35,0.35 0 0 1 12.15,5H11.35A0.35,0.35 0 0 1 11,4.65V3.85A0.35,0.35 0 0 1 11.35,3.5Z " +
            "M11.35,11H12.15A0.35,0.35 0 0 1 12.5,11.35V12.15A0.35,0.35 0 0 1 12.15,12.5H11.35A0.35,0.35 0 0 1 11,12.15V11.35A0.35,0.35 0 0 1 11.35,11Z";

        // decimal places icon
        // ".0" for the overflow menus, in their heavier stroke of 1.5:
        // - the point, 2.5 px round, on the baseline of the zero
        // - the zero, 7 x 12 px with fully round ends
        // even-odd, so the inner outline of the zero is a hole
        private const string DecimalPlacesData =
            "F0 M3.5,11.5A1.25,1.25 0 0 1 4.75,12.75A1.25,1.25 0 0 1 3.5,14A1.25,1.25 0 0 1 2.25,12.75A1.25,1.25 0 0 1 3.5,11.5Z " +
            "M10,2A3.5,3.5 0 0 1 13.5,5.5V10.5A3.5,3.5 0 0 1 10,14A3.5,3.5 0 0 1 6.5,10.5V5.5A3.5,3.5 0 0 1 10,2Z " +
            "M10,3.5A2,2 0 0 1 12,5.5V10.5A2,2 0 0 1 10,12.5A2,2 0 0 1 8,10.5V5.5A2,2 0 0 1 10,3.5Z";


        // === public api ===

        // a new geometry per call; one geometry can sit in one Path only, a second one throws (E_INVALIDARG)
        public static Geometry CreateGeometry(AppIconKind kind)
        {
            string data = kind switch
            {
                AppIconKind.Windows => WindowsData,
                AppIconKind.Lhm => LhmData,
                _ => DecimalPlacesData
            };
            return (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
        }

        // as an icon element, at its own geometry size (a settings header)
        public static PathIcon CreateIcon(AppIconKind kind) => new() { Data = CreateGeometry(kind) };

        // as an icon element the size of the whole grid, so the 16 DIP icon box of an AppBarButton draws it one to one
        // like a font glyph; at the bare geometry size it would scale up to the box
        public static PathIcon CreateGridIcon(AppIconKind kind) => new()
        {
            Data = CreateGeometry(kind),
            Width = GridSize,
            Height = GridSize
        };
    }


    public enum AppIconKind
    {
        Windows,
        Lhm,
        DecimalPlaces
    }


    // the icons in XAML: Icon="{icons:AppIcon Kind=DecimalPlaces}" on an AppBarButton
    [MarkupExtensionReturnType(ReturnType = typeof(PathIcon))]
    public sealed class AppIcon : MarkupExtension
    {
        public AppIconKind Kind { get; set; }

        protected override object ProvideValue() => AppIcons.CreateGridIcon(Kind);
    }
}
