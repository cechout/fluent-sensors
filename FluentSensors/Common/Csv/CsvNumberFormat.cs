namespace FluentSensors.Common.Csv
{
    // the csv number format:
    // separators and decimals; a german Excel or LibreOffice opens "2515.862" as 2515862 (a three digit group), only
    // one or two decimals survive, so the damage looks random
    public enum CsvNumberFormat
    {
        // the regional settings, so a double click opens it right in the local spreadsheet app
        Local,

        // RFC 4180, comma and point everywhere; for scripts, pandas and every non-localized reader
        Invariant
    }
}
