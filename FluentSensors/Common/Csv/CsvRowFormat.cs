using System.Globalization;

using FluentSensors.Persistence.Services;


namespace FluentSensors.Common.Csv
{
    // the csv row format:
    // everything to turn a double into text, resolved once; one snapshot, since a switch midway would format the
    // rows of one file differently
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

        // "0" to "0.000"; fixed, not trimmed ("0.###"), so a column has one width and the settings page one sample
        public string ValueFormat { get; }

        public bool IncludeUnits { get; }


        // === resolution ===

        // Local reads the regional settings, so the file matches the spreadsheet app of the machine
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

            // a list separator equal to the decimal separator is unreadable, every spreadsheet
            // falls back to the semicolon
            if (separator == culture.NumberFormat.NumberDecimalSeparator) separator = ";";

            return new CsvRowFormat(separator, culture, valueFormat, includeUnits);
        }

        private static string BuildValueFormat(int decimalPlaces)
        {
            if (decimalPlaces <= 0) return "0";

            return "0." + new string('0', decimalPlaces);
        }


        // === formatting ===

        // one measurement; the unit only on request, which makes the cell text (for reading, no longer for charting)
        public string FormatValue(double value, string unit)
        {
            string text = value.ToString(ValueFormat, ValueCulture);

            if (!IncludeUnits || string.IsNullOrEmpty(unit)) return text;

            return text + " " + unit;
        }
    }
}
