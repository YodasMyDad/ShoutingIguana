namespace ShoutingIguana.Engine.Export.Tables;

/// <summary>
/// A file of rows under a header, written a row at a time. A cell is text (a string, which no spreadsheet may run as a
/// formula), a number (a long or a double, written as a number), a date and time, true or false, or nothing (null).
/// </summary>
internal interface ITableWriter : IDisposable
{
    /// <summary>The sheets the file has, for a format that has sheets; 0 otherwise.</summary>
    int Sheets { get; }

    /// <summary>The cells whose text was cut to what the format's cell holds.</summary>
    long TruncatedCells { get; }

    /// <summary>Starts a table under its header row: the rows that follow are its own.</summary>
    /// <param name="name">What the table is called, where the format names its tables (a workbook's sheet).</param>
    /// <param name="titles">The header's words, a column each.</param>
    void Begin(string name, IReadOnlyList<string> titles);

    /// <summary>Writes the table's next row, a cell for each column.</summary>
    void WriteRow(ReadOnlySpan<object?> cells);

    /// <summary>Ends the table.</summary>
    void End();

    /// <summary>Writes whatever the format ends with: the file is whole once this returns, and nothing more can be written.</summary>
    void Complete();
}
