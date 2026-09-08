namespace FluentSensors.Common.Csv
{
    // how a recording writes its separators and decimals
    //
    // the distinction is not cosmetic: a german Excel or LibreOffice reads the point in "2515.862" as a thousands
    // separator and opens the value as 2515862, because a three digit group is exactly what a thousands group looks
    // like; only values with one or two decimals survive that, which is why the damage looks random
    public enum CsvNumberFormat
    {
        // separators taken from the machines own regional settings, so the file opens correctly by double click
        // in the spreadsheet app that is installed on it
        Local,

        // RFC 4180: comma separated, point decimal, no matter where the file is written
        // what scripts, pandas and every non-localized reader expect
        Invariant
    }
}
