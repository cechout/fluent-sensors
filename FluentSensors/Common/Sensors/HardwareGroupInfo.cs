using Microsoft.UI.Xaml.Media;

using FluentSensors.Common.UI;


namespace FluentSensors.Common.Sensors
{
    // single source of truth for how a raw LibreHardwareMonitor HardwareType string maps to a broad category (HardwareGroupKind),
    // and what that category displays as (label + icon + accent color)
    // shared by SensorsPage and the PerformancePage so both show identical labels/icons for the same hardware
    public readonly struct HardwareGroupProfile
    {
        public string Label { get; init; }
        public string IconGlyph { get; init; }
        public Windows.UI.Color Color { get; init; }
    }

    public static class HardwareGroupInfo
    {
        // hardwareType here is Hardware.HardwareType.ToString() (e.g. "Cpu", "GpuNvidia", "Memory")
        // named this way to avoid confusion with SensorData.SensorType
        public static HardwareGroupKind GetKind(string hardwareType)
        {
            return hardwareType switch
            {
                "Cpu" => HardwareGroupKind.Cpu,
                "Memory" => HardwareGroupKind.Ram,
                "GpuNvidia" or "GpuAmd" or "GpuIntel" => HardwareGroupKind.Gpu,
                "Storage" => HardwareGroupKind.Storage,
                "Network" => HardwareGroupKind.Network,
                _ => HardwareGroupKind.Other // e.g. Motherboard, Controller
            };
        }

        // the brush every hardware category icon paints itself with, tinted only while hardware icon colours are
        // on; off gives the ordinary foreground, which is what makes the glyph read as a plain white one in the
        // dark theme
        //
        // graph colours deliberately do not come through here; those are resolved per sensor in
        // SensorGraphViewModel and answer to a separate setting
        public static SolidColorBrush GetIconBrush(HardwareGroupKind kind)
        {
            if (HardwareColorMode.UseIconColors)
            {
                return new SolidColorBrush(GetProfile(kind).Color);
            }

            // the untinted case goes through DefaultTextColor instead of reading TextFillColorPrimaryBrush out of
            // Application.Current.Resources: that lookup answers with the light theme value from code behind and
            // never moves again when the theme does, which left these icons as the only elements in the app
            // ignoring a theme switch
            // ForTheme rather than Resolve, because the theme that counts is the one actually applied to the page,
            // not the app theme setting, see HardwareColorMode.IsDarkTheme
            return DefaultTextColor.ForTheme(HardwareColorMode.IsDarkTheme) as SolidColorBrush
                ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        }

        public static HardwareGroupProfile GetProfile(HardwareGroupKind kind)
        {
            return kind switch
            {
                HardwareGroupKind.Cpu => new HardwareGroupProfile { Label = "CPU", IconGlyph = "\uE950", Color = Windows.UI.Color.FromArgb(0xFF, 0x00, 0xB7, 0xC3) },
                HardwareGroupKind.Ram => new HardwareGroupProfile { Label = "RAM", IconGlyph = "\uE964", Color = Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4) },
                HardwareGroupKind.Gpu => new HardwareGroupProfile { Label = "GPU", IconGlyph = "\uF211", Color = Windows.UI.Color.FromArgb(0xFF, 0xA2, 0x58, 0xB6) },
                HardwareGroupKind.Storage => new HardwareGroupProfile { Label = "Storage", IconGlyph = "\uEDA2", Color = Windows.UI.Color.FromArgb(0xFF, 0x90, 0xC2, 0x42) },
                // only one icon for now, no Ethernet/Wi-Fi distinction yet (possible future refinement)
                HardwareGroupKind.Network => new HardwareGroupProfile { Label = "Network", IconGlyph = "\uE839", Color = Windows.UI.Color.FromArgb(0xFF, 0xBF, 0x59, 0x77) },
                _ => new HardwareGroupProfile { Label = "Other", IconGlyph = "\uEA1F", Color = Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80) } // placeholder color, none specified yet
            };
        }
    }
}
