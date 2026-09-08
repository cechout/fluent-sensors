using System.Globalization;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Common.Csv
{
    // everything a recorded row needs to turn a double into text, resolved once from the settings
    //
    // bundled into one object because a recording has to snapshot all of it together: switching any single piece
    // midway through an open file would leave the rows before and after it formatted differently, and nothing
    // reading the file would notice
    public sealed class CsvRowFormat
    {
        private CsvRowFormat(string separator, CultureInfo valueCulture, string valueFormat, bool includeUnits)
        {
            Separator = separator;
            ValueCulture = valueCulture;
            ValueFormat = valueFormat;
            IncludeUnits = includeUnits;
        }


        // === resolved pieces ===

        public string Separator { get; }
        public CultureInfo ValueCulture { get; }

        // "0", "0.0", "0.00" or "0.000"
        // fixed rather than trimming ("0.###"), so every cell of a column is the same width and the settings page
        // can show one sample that stands for all of them
        public string ValueFormat { get; }

        public bool IncludeUnits { get; }


        // === resolution ===

        // resolves the configured format into the pieces a row actually needs
        // Local reads the machines own regional settings rather than hardcoding german, so the file matches
        // whatever spreadsheet app is installed on the system that wrote it
        public static CsvRowFormat Resolve()
        {
            var settings = SettingsService.Instance;

            string valueFormat = BuildValueFormat(settings.CsvDecimalPlaces);
            bool includeUnits = settings.CsvIncludeUnits;

            if (settings.CsvNumberFormat == CsvNumberFormat.Invariant)
            {
                return new CsvRowFormat(",", CultureInfo.InvariantCulture, valueFormat, includeUnits);
            }

            var culture = CultureInfo.CurrentCulture;
            string separator = culture.TextInfo.ListSeparator;

            // a locale whose list separator is also its decimal separator would write rows nothing can read back;
            // the semicolon is what every spreadsheet falls back to in that case
            if (separator == culture.NumberFormat.NumberDecimalSeparator) separator = ";";

            return new CsvRowFormat(separator, culture, valueFormat, includeUnits);
        }

        private static string BuildValueFormat(int decimalPlaces)
        {
            if (decimalPlaces <= 0) return "0";

            return "0." + new string('0', decimalPlaces);
        }


        // === formatting ===

        // one measurement as it lands in the file
        // the unit is appended only when it was asked for; note that this turns the cell into text, so a file
        // written that way is for reading and no longer for charting
        public string FormatValue(double value, string unit)
        {
            string text = value.ToString(ValueFormat, ValueCulture);

            if (!IncludeUnits || string.IsNullOrEmpty(unit)) return text;

            return text + " " + unit;
        }
    }
}
