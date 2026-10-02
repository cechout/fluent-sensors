using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI;


namespace FluentSensors.Persistence.Services
{
    // Windows.UI.Color has fields, not properties, which System.Text.Json skips; written as "#AARRGGBB"
    public class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string hex = reader.GetString();
            byte a = Convert.ToByte(hex.Substring(1, 2), 16);
            byte r = Convert.ToByte(hex.Substring(3, 2), 16);
            byte g = Convert.ToByte(hex.Substring(5, 2), 16);
            byte b = Convert.ToByte(hex.Substring(7, 2), 16);
            return Color.FromArgb(a, r, g, b);
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");
        }
    }
}
