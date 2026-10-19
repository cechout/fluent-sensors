using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;


namespace FluentSensors.Features.WidgetBoard
{
    // one sensor as the card shows it; the PNGs in both themes, the board picks by its own
    public sealed record WidgetBoardCardSensor(string Name, string Value, byte[] DarkGraph, byte[] LightGraph);

    // the adaptive card of one widget size:
    // one container per sensor sharing the height, the graph as its background and name and value as text on top
    // (like the overlay of SensorGraphControl), the time range under all graphs
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
        private const float TimeRowHeight = 16; // one small text line
        private const float TimeRowSpacing = 4; // the small spacing above it
        private const float BottomSpacer = 8; // lifts the time off the card edge

        // the board crops a graph that is a hair wider than its box at the right edge; drawn this much taller, with
        // a transparent strip on top, the overhang goes to the strip
        public const float TopInsetShare = 0.02f;

        // the edge columns inset the name and the value inside the graph card
        private const string EdgeColumn = "{\"type\":\"Column\",\"width\":\"6px\"}";


        // === public api ===

        public static int SensorSlots(byte size) => size switch
        {
            WidgetBoardProtocol.Small => 1,
            WidgetBoardProtocol.Medium => 2,
            _ => 3
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

            float graphs = content - TimeRowSpacing - TimeRowHeight - BottomSpacer - Spacing * (count - 1);
            return (ContentWidth, graphs / Math.Max(1, count));
        }

        public static string BuildTemplate(int count)
        {
            var body = new List<string>();

            for (int i = 0; i < count; i++)
            {
                string labels = "{\"type\":\"ColumnSet\",\"columns\":[" + EdgeColumn + "," +
                    $"{{\"type\":\"Column\",\"width\":\"stretch\",\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{n{i}}}\",\"size\":\"small\",\"isSubtle\":true,\"wrap\":false}}]}}," +
                    $"{{\"type\":\"Column\",\"width\":\"auto\",\"items\":[{{\"type\":\"TextBlock\",\"text\":\"${{v{i}}}\",\"size\":\"small\",\"wrap\":false}}]}}," +
                    EdgeColumn + "]}";

                // a theme switch swaps the whole container, the background url cannot carry a condition
                foreach (bool isLight in new[] { false, true })
                {
                    string spacing = i == 0 ? "" : ",\"spacing\":\"default\"";
                    string when = isLight ? "${$host.isLightTheme}" : "${!$host.isLightTheme}";
                    string image = $"${{{(isLight ? "l" : "d")}{i}}}";

                    body.Add($"{{\"type\":\"Container\",\"height\":\"stretch\"{spacing},\"$when\":\"{when}\"," +
                        $"\"backgroundImage\":{{\"url\":\"{image}\",\"fillMode\":\"cover\",\"verticalAlignment\":\"bottom\"}}," +
                        $"\"items\":[{{\"type\":\"Container\",\"minHeight\":\"2px\"}},{labels}]}}");
                }
            }

            body.Add("{\"type\":\"TextBlock\",\"text\":\"${time}\",\"size\":\"small\",\"isSubtle\":true,\"spacing\":\"small\",\"wrap\":false}");
            body.Add($"{{\"type\":\"Container\",\"minHeight\":\"{BottomSpacer:0}px\",\"spacing\":\"none\"}}");

            return "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"body\":[" + string.Join(",", body) + "]}";
        }

        public static string BuildData(IReadOnlyList<WidgetBoardCardSensor> sensors, string timeText)
        {
            var data = new StringBuilder("{");

            for (int i = 0; i < sensors.Count; i++)
            {
                var sensor = sensors[i];
                data.Append($"\"n{i}\":\"{JsonEncodedText.Encode(sensor.Name)}\",");
                data.Append($"\"v{i}\":\"{JsonEncodedText.Encode(sensor.Value)}\",");
                data.Append($"\"d{i}\":\"data:image/png;base64,{Convert.ToBase64String(sensor.DarkGraph)}\",");
                data.Append($"\"l{i}\":\"data:image/png;base64,{Convert.ToBase64String(sensor.LightGraph)}\",");
            }

            data.Append($"\"time\":\"{JsonEncodedText.Encode(timeText)}\"}}");
            return data.ToString();
        }

        // a single line in the middle of the card, for no selected sensors
        public static (string Template, string Data) BuildNote(string text)
        {
            const string template =
                "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"verticalContentAlignment\":\"Center\",\"body\":[" +
                "{\"type\":\"TextBlock\",\"text\":\"${text}\",\"wrap\":true,\"isSubtle\":true,\"horizontalAlignment\":\"Center\"}]}";

            return (template, $"{{\"text\":\"{JsonEncodedText.Encode(text)}\"}}");
        }
    }
}
