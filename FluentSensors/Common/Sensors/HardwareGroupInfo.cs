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
        public Windows.UI.Color Color { get; init; }
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
                return new SolidColorBrush(GetProfile(kind).Color);
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
                HardwareGroupKind.Cpu => new HardwareGroupProfile { Label = "CPU", IconGlyph = HasFluentIconGlyphs ? CpuGlyph : CpuFallbackGlyph, Color = Windows.UI.Color.FromArgb(0xFF, 0x00, 0xB7, 0xC3) },
                HardwareGroupKind.Ram => new HardwareGroupProfile { Label = "RAM", IconGlyph = HasFluentIconGlyphs ? RamGlyph : RamFallbackGlyph, Color = Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4) },
                HardwareGroupKind.Gpu => new HardwareGroupProfile { Label = "GPU", IconGlyph = "\uF211", Color = Windows.UI.Color.FromArgb(0xFF, 0xA2, 0x58, 0xB6) },
                HardwareGroupKind.Storage => new HardwareGroupProfile { Label = "Storage", IconGlyph = "\uEDA2", Color = Windows.UI.Color.FromArgb(0xFF, 0x90, 0xC2, 0x42) },
                // the category glyph is the wired one; a wireless adapter swaps it, see GetNetworkIconGlyph
                HardwareGroupKind.Network => new HardwareGroupProfile { Label = "Network", IconGlyph = WiredNetworkGlyph, Color = Windows.UI.Color.FromArgb(0xFF, 0xBF, 0x59, 0x77) },
                _ => new HardwareGroupProfile { Label = "Other", IconGlyph = "\uEA1F", Color = Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80) } // placeholder color
            };
        }

        // the one glyph per device: wireless shows wi-fi, wired or without a WMI match keeps the category glyph
        public static string GetNetworkIconGlyph(NetworkInterfaceType? interfaceType) =>
            interfaceType == NetworkInterfaceType.Wireless80211 ? WirelessNetworkGlyph : WiredNetworkGlyph;
    }
}
