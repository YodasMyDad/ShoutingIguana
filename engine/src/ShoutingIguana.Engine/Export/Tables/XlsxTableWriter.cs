using System.Globalization;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Xlsx;
using ShoutingIguana.Engine.Export.Xlsx.Models;

namespace ShoutingIguana.Engine.Export.Tables;

/// <summary>
/// Tables as the sheets of a workbook: each under a bold header row that stays in view and filters, its columns as wide
/// as its first rows need (not every cell is measured: a table's first rows are held until the widths are known, then
/// written). A sheet holds 1,048,576 rows, so a longer table goes on to further sheets, each under the same header
/// ("Links", "Links (2)"), up to the sheets a table is allowed; past that the export stops rather than write a file
/// short of its rows.
/// </summary>
/// <param name="maxSheetsPerTable">The sheets one table's rows may fill.</param>
internal sealed class XlsxTableWriter(XlsxWriter workbook, int maxSheetsPerTable) : ITableWriter
{
    /// <summary>The rows a table's column widths are measured from.</summary>
    private const int MEASURED_ROWS = 200;

    private const double NARROWEST = 9;
    private const double WIDEST = 80;

    /// <summary>Room for a header's filter button.</summary>
    private const int HEADER_ROOM = 3;

    private readonly List<string> _names = [];
    private readonly List<object?[]> _held = [];
    private IReadOnlyList<string> _titles = [];
    private IReadOnlyList<double> _widths = [];
    private XlsxCell[] _cells = [];
    private string _name = string.Empty;
    private bool _open;
    private bool _measuring;
    private int _continuations;

    public int Sheets => workbook.Sheets;

    public long TruncatedCells => workbook.TruncatedCells;

    /// <summary>The workbook under the tables, for a sheet that isn't one (a summary).</summary>
    public XlsxWriter Workbook => workbook;

    /// <summary>The name a sheet would get, told apart from those the workbook has.</summary>
    public string NameFor(string wanted) => XlsxWriter.SheetName(wanted, _names);

    /// <summary>Takes a name for a sheet written straight to the <see cref="Workbook"/>, so no table is given the same one.</summary>
    public string Reserve(string wanted)
    {
        var name = NameFor(wanted);
        _names.Add(name);
        return name;
    }

    public void Begin(string name, IReadOnlyList<string> titles)
    {
        if (_open)
        {
            throw new InvalidOperationException("The table before must be ended first.");
        }

        (_name, _titles, _cells, _open, _measuring, _continuations) = (name, titles, new XlsxCell[titles.Count], true, true, 0);
        _held.Clear();
    }

    public void WriteRow(ReadOnlySpan<object?> cells)
    {
        if (_measuring)
        {
            _held.Add(cells.ToArray());
            if (_held.Count >= MEASURED_ROWS)
            {
                StartSheets();
            }

            return;
        }

        Write(cells);
    }

    public void End()
    {
        if (_measuring)
        {
            StartSheets();
        }

        workbook.EndSheet();
        _open = false;
    }

    public void Complete() => workbook.Complete();

    public void Dispose() => workbook.Dispose();

    /// <summary>The widths are known: the first sheet begins, and the rows held go into it.</summary>
    private void StartSheets()
    {
        _measuring = false;
        _widths = Widths();
        BeginSheet(_name);
        foreach (var row in _held)
        {
            Write(row);
        }

        _held.Clear();
    }

    private void Write(ReadOnlySpan<object?> cells)
    {
        if (workbook.RowsInSheet >= XlsxWriter.MAX_ROWS)
        {
            workbook.EndSheet();
            _continuations++;
            BeginSheet(string.Create(CultureInfo.InvariantCulture, $"{_name} ({_continuations + 1})"));
        }

        for (var index = 0; index < _cells.Length; index++)
        {
            _cells[index] = index < cells.Length ? Cell(cells[index]) : XlsxCell.Empty;
        }

        workbook.WriteRow(_cells);
    }

    private void BeginSheet(string wanted)
    {
        if (_continuations >= maxSheetsPerTable)
        {
            throw new ExportException(ExportFailure.TooLarge, TooLarge(maxSheetsPerTable));
        }

        var name = Reserve(wanted);
        workbook.BeginSheet(name, _widths, frozenRows: 1, filter: true);
        for (var index = 0; index < _cells.Length; index++)
        {
            _cells[index] = XlsxCell.Text(_titles[index], XlsxStyle.Bold);
        }

        workbook.WriteRow(_cells);
    }

    /// <summary>What an export too long for a workbook is told.</summary>
    public static string TooLarge(int sheets) => string.Create(CultureInfo.InvariantCulture,
        $"This is more than an Excel file can hold here: {sheets} sheets of {XlsxWriter.MAX_ROWS - 1:N0} rows. Export it as CSV, which has no limit, or narrow it with a filter.");

    private static XlsxCell Cell(object? value) => value switch
    {
        null => XlsxCell.Empty,
        string text => XlsxCell.Text(text),
        long whole => XlsxCell.Number(whole),
        int whole => XlsxCell.Number(whole),
        double real => XlsxCell.Number(real),
        bool flag => XlsxCell.Text(flag ? Storage.ColumnWords.YES : Storage.ColumnWords.NO),
        DateTime moment => XlsxCell.DateTime(moment),
        _ => XlsxCell.Text(Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    /// <summary>Each column as wide as its header and the longest of its first cells, within reason.</summary>
    private double[] Widths()
    {
        var widths = new double[_titles.Count];
        for (var column = 0; column < widths.Length; column++)
        {
            var longest = _titles[column].Length + HEADER_ROOM;
            foreach (var row in _held)
            {
                if (column < row.Length)
                {
                    longest = Math.Max(longest, Length(row[column]));
                }
            }

            widths[column] = Math.Clamp(longest + 1, NARROWEST, WIDEST);
        }

        return widths;
    }

    private static int Length(object? value) => value switch
    {
        null => 0,
        string text => text.Length,
        DateTime => 16,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Length ?? 0
    };
}
