namespace FluentSensors.Common.Csv
{
    // what a resumed recording writes at the seam where the pause was
    //
    // the distinction only matters to whatever reads the file afterwards: a chart drawn straight through the seam
    // claims a measurement that was never taken, while a broken line says the truth
    public enum CsvPauseSeam
    {
        // one row of empty fields, so a spreadsheet breaks its line there instead of interpolating across the pause
        // (deliberately empty fields and not an empty line: the column count has to survive, and a truly blank line
        // ends the data range on import)
        Gap,

        // the next measurement follows the last one directly; the pause is only visible in the timestamps
        Seamless
    }
}
