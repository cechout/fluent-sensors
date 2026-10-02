namespace FluentSensors.Common.Csv
{
    // the csv pause seam:
    // what a resumed recording writes where the pause was; a chart drawn through it claims a measurement never taken
    public enum CsvPauseSeam
    {
        // a row of empty fields breaks the chart line; (not a blank line, which ends the data range on import)
        Gap,

        // the pause shows only in the timestamps
        Seamless
    }
}
