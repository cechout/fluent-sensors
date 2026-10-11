namespace FluentSensors.Common.UI
{
    // one colour in its two theme values:
    // (the type and hardware palettes currently carry the same value in both)
    public readonly record struct ThemeColorPair(Windows.UI.Color Dark, Windows.UI.Color Light)
    {
        // the same value in both themes (the accent, a custom colour)
        public ThemeColorPair(Windows.UI.Color color) : this(color, color) { }

        public Windows.UI.Color ForTheme(bool isDarkTheme) => isDarkTheme ? Dark : Light;

        // 0xRRGGBB, opaque
        public static ThemeColorPair FromRgb(uint dark, uint light) => new(ToColor(dark), ToColor(light));

        private static Windows.UI.Color ToColor(uint rgb) =>
            Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
