using Microsoft.UI.Xaml.Media;
using System;
using System.Net.NetworkInformation;

using FluentSensors.Common.UI;


namespace FluentSensors.Common.Sensors
{
    public readonly struct HardwareGroupProfile
    {
        public string Label { get; init; }
        public string IconGlyph { get; init; }
        public ThemeColorPair Colors { get; init; }
    }

    // the hardware categories:
    // maps a raw LHM HardwareType to a HardwareGroupKind with its label, icon and color, one source for the
    // sensors and performance pages
    public static class HardwareGroupInfo
    {
        // === glyphs ===

        // the cpu and ram glyphs exist only in Segoe Fluent Icons; Windows 10 falls back to Segoe
        // MDL2 Assets, an empty box there
        private static readonly bool HasFluentIconGlyphs = Environment.OSVersion.Version.Build >= 22000;

        private const string CpuGlyph = "\uEEA1";
        private const string CpuFallbackGlyph = "\uE950"; // windows 10
        private const string RamGlyph = "\uEEA0";
        private const string RamFallbackGlyph = "\uE964"; // windows 10
        private const string WiredNetworkGlyph = "\uE839";
        private const string WirelessNetworkGlyph = "\uE701";


        // Hardware.HardwareType.ToString() ("Cpu", "GpuNvidia", "Memory"); named apart from SensorData.SensorType
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

        // the category icon brush: tinted with icon colours on, the plain foreground off; (graph colours come from
        // SensorGraphViewModel, another setting)
        public static SolidColorBrush GetIconBrush(HardwareGroupKind kind)
        {
            if (HardwareColorMode.UseIconColors)
            {
                return new SolidColorBrush(GetProfile(kind).Colors.ForTheme(HardwareColorMode.IsDarkTheme));
            }

            // DefaultTextColor, since TextFillColorPrimaryBrush from Application.Current.Resources answers the light
            // value in code behind and never follows the theme
            // ForTheme, since the applied page theme counts, not the setting (see HardwareColorMode.IsDarkTheme)
            return DefaultTextColor.ForTheme(HardwareColorMode.IsDarkTheme) as SolidColorBrush
                ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        }

        public static HardwareGroupProfile GetProfile(HardwareGroupKind kind)
        {
            return kind switch
            {
                HardwareGroupKind.Cpu => new HardwareGroupProfile { Label = "CPU", IconGlyph = HasFluentIconGlyphs ? CpuGlyph : CpuFallbackGlyph, Colors = ThemeColorPair.FromRgb(0x02B3BF, 0x02B3BF) },
                HardwareGroupKind.Ram => new HardwareGroupProfile { Label = "RAM", IconGlyph = HasFluentIconGlyphs ? RamGlyph : RamFallbackGlyph, Colors = ThemeColorPair.FromRgb(0x4FA2F8, 0x4FA2F8) },
                HardwareGroupKind.Gpu => new HardwareGroupProfile { Label = "GPU", IconGlyph = "\uF211", Colors = ThemeColorPair.FromRgb(0xC67ED9, 0xC67ED9) },
                HardwareGroupKind.Storage => new HardwareGroupProfile { Label = "Storage", IconGlyph = "\uEDA2", Colors = ThemeColorPair.FromRgb(0x83AF3E, 0x83AF3E) },
                // the category glyph is the wired one; a wireless adapter swaps it, see GetNetworkIconGlyph
                HardwareGroupKind.Network => new HardwareGroupProfile { Label = "Network", IconGlyph = WiredNetworkGlyph, Colors = ThemeColorPair.FromRgb(0xE97295, 0xE97295) },
                _ => new HardwareGroupProfile { Label = "Other", IconGlyph = "\uEA1F", Colors = ThemeColorPair.FromRgb(0x9E9E9E, 0x9E9E9E) } // placeholder color
            };
        }

        // the one glyph per device: wireless shows wi-fi, wired or without a WMI match keeps the category glyph
        public static string GetNetworkIconGlyph(NetworkInterfaceType? interfaceType) =>
            interfaceType == NetworkInterfaceType.Wireless80211 ? WirelessNetworkGlyph : WiredNetworkGlyph;
    }
}
