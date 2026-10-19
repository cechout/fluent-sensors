using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using FluentSensors.Common.Localization;


namespace FluentSensors.Features.WidgetBoard
{
    // one sensor as the card shows it; YMax the shown top of the y-axis, the PNGs in both themes, the board picks by
    // its own
    public sealed record WidgetBoardCardSensor(string Name, string YMax, string Value, byte[] DarkGraph, byte[] LightGraph);

    // the adaptive card of one widget size:
    // one container per sensor sharing the height, name and value in a row over the graph like the status row of
    // SensorPanelControl, under all graphs the time range and the links to the sensors page and the settings
    public static class WidgetBoardCard
    {
        // === fields ===

        // in dip; the content box of a widget, measured on the board at 175 %
        public const float ContentWidth = 269;
        private const float ContentHeightSmall = 99;
        private const float ContentHeightMedium = 260;
        private const float ContentHeightLarge = 419;

        // in dip; what sits between and under the sensors
        private const float Spacing = 8; // the default spacing of the board between two containers
        private const float TextRowHeight = 16; // one small text line; a status row, the time, the links
        private const float StatusRowGap = 3; // status row to graph, the row spacing of SensorPanelControl
        private const float TextRowSpacing = 4; // the small spacing above the time and above the links
        private const float BottomSpacer = 8; // lifts the links off the card edge

        // --- text gaps ---
        // in px (dip); spacer columns, every other column of a row says spacing none, the board would put its own
        // default spacing (8) in front of it otherwise
        private const int TextInset = 2; // every text row from the card edge: status rows, time, links
        private const int YMaxValueGap = 10; // y-max to current value
        private const int LinkGap = 12; // between the two links


        // === public api ===

        public static int SensorSlots(byte size) => size switch
        {
            WidgetBoardProtocol.Small => 1,
            WidgetBoardProtocol.Medium => 2,
            _ => 4
        };

        // the dip box one graph gets with count sensors on the card
        public static (float Width, float Height) GraphBox(byte size, int count)
        {
            float content = size switch
            {
                WidgetBoardProtocol.Small => ContentHeightSmall,
                WidgetBoardProtocol.Medium => ContentHeightMedium,
                _ => ContentHeightLarge
            };

            float sensors = content - 2 * (TextRowSpacing + TextRowHeight) - BottomSpacer - Spacing * (count - 1);
            return (ContentWidth, sensors / Math.Max(1, count) - TextRowHeight - StatusRowGap);
        }

        public static string BuildTemplate(int count)
        {
            var body = new List<string>();

            for (int i = 0; i < count; i++)
            {
                string statusRow = "{\"type\":\"ColumnSet\",\"columns\":[" + Gap(TextInset) + "," +
                    $"{{\"type\":\"Column\",\"width\":\"stretch\",\"spacing\":\"none\",\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{n{i}}}\",\"size\":\"small\",\"wrap\":false}}]}}," +
                    $"{{\"type\":\"Column\",\"width\":\"auto\",\"spacing\":\"none\",\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{y{i}}}\",\"size\":\"small\",\"isSubtle\":true,\"wrap\":false}}]}}," +
                    Gap(YMaxValueGap) + "," +
                    $"{{\"type\":\"Column\",\"width\":\"auto\",\"spacing\":\"none\",\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{v{i}}}\",\"size\":\"small\",\"wrap\":false}}]}}," +
                    Gap(TextInset) + "]}";
                string gap = $"{{\"type\":\"Container\",\"minHeight\":\"{StatusRowGap:0}px\",\"spacing\":\"none\"}}";

                // a theme switch swaps the whole sensor, the image url cannot carry a condition
                foreach (bool isLight in new[] { false, true })
                {
                    string spacing = i == 0 ? "" : ",\"spacing\":\"default\"";
                    string when = isLight ? "${$host.isLightTheme}" : "${!$host.isLightTheme}";
                    string image = $"${{{(isLight ? "l" : "d")}{i}}}";
                    string graph = $"{{\"type\":\"Image\",\"url\":\"{image}\",\"height\":\"stretch\",\"spacing\":\"none\",\"horizontalAlignment\":\"center\"}}";

                    body.Add($"{{\"type\":\"Container\",\"height\":\"stretch\"{spacing},\"$when\":\"{when}\"," +
                        $"\"items\":[{statusRow},{gap},{graph}]}}");
                }
            }

            body.Add("{\"type\":\"ColumnSet\",\"spacing\":\"small\",\"columns\":[" + Gap(TextInset) + "," +
                "{\"type\":\"Column\",\"width\":\"stretch\",\"spacing\":\"none\",\"items\":[{\"type\":\"TextBlock\",\"text\":\"${time}\",\"size\":\"small\",\"isSubtle\":true,\"wrap\":false}]}]}");

            // plain accent text as buttons, side by side: the select action of an auto column, so only the text
            // takes the click
            body.Add("{\"type\":\"ColumnSet\",\"spacing\":\"small\",\"columns\":[" + Gap(TextInset) + "," +
                LinkColumn("sensorsLink", WidgetBoardProtocol.OpenSensorsVerb) + "," +
                Gap(LinkGap) + "," +
                LinkColumn("settingsLink", WidgetBoardProtocol.OpenSettingsVerb) + "]}");
            body.Add($"{{\"type\":\"Container\",\"minHeight\":\"{BottomSpacer:0}px\",\"spacing\":\"none\"}}");

            return "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"body\":[" + string.Join(",", body) + "]}";
        }

        public static string BuildData(IReadOnlyList<WidgetBoardCardSensor> sensors, string timeText, string sensorsLinkText, string settingsLinkText)
        {
            var data = new StringBuilder("{");

            for (int i = 0; i < sensors.Count; i++)
            {
                var sensor = sensors[i];
                data.Append($"\"n{i}\":\"{JsonEncodedText.Encode(sensor.Name)}\",");
                data.Append($"\"y{i}\":\"{JsonEncodedText.Encode(sensor.YMax)}\",");
                data.Append($"\"v{i}\":\"{JsonEncodedText.Encode(sensor.Value)}\",");
                data.Append($"\"d{i}\":\"data:image/png;base64,{Convert.ToBase64String(sensor.DarkGraph)}\",");
                data.Append($"\"l{i}\":\"data:image/png;base64,{Convert.ToBase64String(sensor.LightGraph)}\",");
            }

            data.Append($"\"time\":\"{JsonEncodedText.Encode(timeText)}\",");
            data.Append($"\"sensorsLink\":\"{JsonEncodedText.Encode(sensorsLinkText)}\",");
            data.Append($"\"settingsLink\":\"{JsonEncodedText.Encode(settingsLinkText)}\"}}");
            return data.ToString();
        }

        // the shown top of the y-axis, "max: 47,8 %"; the card and the settings page
        public static string FormatYMax(string value, string unit) =>
            AppStrings.Format("WidgetBoard_YMax", string.IsNullOrEmpty(unit) ? value : $"{value} {unit}");

        // a single line in the middle of the card, for no selected sensors
        public static (string Template, string Data) BuildNote(string text)
        {
            const string template =
                "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"verticalContentAlignment\":\"Center\",\"body\":[" +
                "{\"type\":\"TextBlock\",\"text\":\"${text}\",\"wrap\":true,\"isSubtle\":true,\"horizontalAlignment\":\"Center\"}]}";

            return (template, $"{{\"text\":\"{JsonEncodedText.Encode(text)}\"}}");
        }


        // === private helpers ===

        private static string Gap(int px) => $"{{\"type\":\"Column\",\"width\":\"{px}px\",\"spacing\":\"none\"}}";

        private static string LinkColumn(string textKey, string verb) =>
            "{\"type\":\"Column\",\"width\":\"auto\",\"spacing\":\"none\"," +
            $"\"selectAction\":{{\"type\":\"Action.Execute\",\"verb\":\"{verb}\"}}," +
            $"\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{{textKey}}}\",\"size\":\"small\",\"color\":\"accent\",\"wrap\":false}}]}}";
    }
}
