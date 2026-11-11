using System.Globalization;
using System.IO.Compression;
using System.Text;
using ShoutingIguana.Engine.Export.Xlsx.Models;

namespace ShoutingIguana.Engine.Export.Xlsx;

/// <summary>
/// Writes an Excel workbook (.xlsx) a row at a time, holding none of it: each sheet's rows go straight into the file's
/// ZIP as they're given, and the texts into a shared string table beside it (<see cref="SharedStrings"/>), so a sheet of
/// a million rows costs the memory of one. The file is the plain form of the format, as Excel writes it itself: shared
/// strings, every row and cell named by its place, numbers as numbers, and a small fixed set of styles
/// (<see cref="XlsxStyle"/>), so that Excel, LibreOffice and Numbers open it without repairing it.
/// </summary>
internal sealed class XlsxWriter : IDisposable
{
    /// <summary>The rows a sheet holds, its header among them.</summary>
    public const int MAX_ROWS = 1_048_576;

    /// <summary>The characters a sheet's name holds.</summary>
    public const int MAX_SHEET_NAME = 31;

    private const int BUFFER_BYTES = 1 << 16;
    private const string MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RELATIONSHIPS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PACKAGE_RELATIONSHIPS = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DECLARATION = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    /// <summary>The formats cells name by index, in the order of <see cref="XlsxStyle"/>.</summary>
    private const string STYLES =
        DECLARATION +
        "<styleSheet xmlns=\"" + MAIN + "\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm\"/></numFmts>" +
        "<fonts count=\"3\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "</fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"7\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" quotePrefix=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"10\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static readonly UTF8Encoding UTF8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>What a sheet's name can't hold.</summary>
    private static readonly char[] NAME_FORBIDDEN = ['\\', '/', '?', '*', '[', ']', ':'];

    private readonly ZipArchive _archive;
    private readonly CompressionLevel _compression;
    private readonly SharedStrings _strings;
    private readonly List<string> _sheets = [];
    private readonly List<string> _letters = [];
    private StreamWriter? _sheet;
    private int _rows;
    private int _widest;
    private bool _filter;
    private bool _completed;

    /// <param name="output">Where the workbook goes; left open.</param>
    /// <param name="stringsFile">A file of its own for the shared strings as they're written (<see cref="SharedStrings"/>); closed with this.</param>
    public XlsxWriter(Stream output, Stream stringsFile, CompressionLevel compression = CompressionLevel.Fastest)
    {
        _archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        _compression = compression;
        _strings = new SharedStrings(stringsFile);
    }

    /// <summary>The sheets begun so far.</summary>
    public int Sheets => _sheets.Count;

    /// <summary>The rows written to the sheet that's open.</summary>
    public int RowsInSheet => _rows;

    /// <summary>The cells whose text was cut to what a cell holds.</summary>
    public long TruncatedCells { get; private set; }

    /// <summary>
    /// A name a sheet can have, from the one wanted: what a name can't hold is taken out, it's cut to 31 characters (ending
    /// "…" where it was), and it's told apart from the names already taken, which differ by more than case.
    /// </summary>
    public static string SheetName(string wanted, IEnumerable<string> taken)
    {
        var cleaned = new StringBuilder(wanted.Length);
        foreach (var character in wanted)
        {
            cleaned.Append(char.IsControl(character) || char.IsSurrogate(character) || Array.IndexOf(NAME_FORBIDDEN, character) >= 0 ? ' ' : character);
        }

        var name = cleaned.ToString().Trim().Trim('\'').Trim();
        if (name.Length == 0)
        {
            name = "Sheet";
        }

        // Excel keeps this name for itself.
        if (name.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            name += "_";
        }

        name = Cut(name, MAX_SHEET_NAME);
        HashSet<string> used = new(taken, StringComparer.OrdinalIgnoreCase);
        var unique = name;
        for (var copy = 2; used.Contains(unique); copy++)
        {
            var suffix = string.Create(CultureInfo.InvariantCulture, $" ({copy})");
            unique = Cut(name, MAX_SHEET_NAME - suffix.Length).TrimEnd() + suffix;
        }

        return unique;
    }

    /// <summary>Starts a sheet: the rows that follow are its own.</summary>
    /// <param name="name">Its name, as <see cref="SheetName"/> gives one.</param>
    /// <param name="widths">Each column's width, in characters; none leaves every column at the default.</param>
    /// <param name="frozenRows">The rows at the top that stay in view as the sheet scrolls.</param>
    /// <param name="filter">Whether the first row gets a filter button on each column.</param>
    public void BeginSheet(string name, IReadOnlyList<double> widths, int frozenRows = 0, bool filter = false)
    {
        if (_sheet is not null)
        {
            throw new InvalidOperationException("The sheet before must be ended first.");
        }

        _sheets.Add(name);
        var entry = _archive.CreateEntry(string.Create(CultureInfo.InvariantCulture, $"xl/worksheets/sheet{_sheets.Count}.xml"), _compression);
        var sheet = _sheet = new StreamWriter(entry.Open(), UTF8, BUFFER_BYTES);
        (_rows, _widest, _filter) = (0, 0, filter);

        sheet.Write(DECLARATION);
        sheet.Write("<worksheet xmlns=\"" + MAIN + "\"><sheetViews><sheetView workbookViewId=\"0\"");
        if (_sheets.Count == 1)
        {
            sheet.Write(" tabSelected=\"1\"");
        }

        if (frozenRows > 0)
        {
            var below = string.Create(CultureInfo.InvariantCulture, $"A{frozenRows + 1}");
            sheet.Write(string.Create(CultureInfo.InvariantCulture,
                $"><pane ySplit=\"{frozenRows}\" topLeftCell=\"{below}\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"{below}\" sqref=\"{below}\"/></sheetView>"));
        }
        else
        {
            sheet.Write("/>");
        }

        sheet.Write("</sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
        if (widths.Count > 0)
        {
            sheet.Write("<cols>");
            for (var column = 0; column < widths.Count; column++)
            {
                sheet.Write(string.Create(CultureInfo.InvariantCulture, $"<col min=\"{column + 1}\" max=\"{column + 1}\" width=\"{widths[column]:0.##}\" customWidth=\"1\"/>"));
            }

            sheet.Write("</cols>");
        }

        sheet.Write("<sheetData>");
    }

    /// <summary>Writes the next row of the sheet that's open; a cell that holds nothing isn't written.</summary>
    /// <exception cref="InvalidOperationException">No sheet is open, or it's full (<see cref="MAX_ROWS"/>).</exception>
    public void WriteRow(ReadOnlySpan<XlsxCell> cells)
    {
        var sheet = _sheet ?? throw new InvalidOperationException("No sheet has been begun.");
        if (_rows >= MAX_ROWS)
        {
            throw new InvalidOperationException("The sheet is full.");
        }

        _rows++;
        Span<char> digits = stackalloc char[32];
        _rows.TryFormat(digits, out var rowLength, default, CultureInfo.InvariantCulture);
        var row = digits[..rowLength];
        Span<char> number = stackalloc char[32];

        sheet.Write("<row r=\"");
        sheet.Write(row);
        sheet.Write("\">");
        for (var column = 0; column < cells.Length; column++)
        {
            var cell = cells[column];
            if (cell.Kind == XlsxCellKind.Empty)
            {
                continue;
            }

            _widest = Math.Max(_widest, column + 1);
            sheet.Write("<c r=\"");
            sheet.Write(Letters(column));
            sheet.Write(row);
            if (cell.Style != XlsxStyle.None)
            {
                sheet.Write("\" s=\"");
                ((int)cell.Style).TryFormat(number, out var styleLength, default, CultureInfo.InvariantCulture);
                sheet.Write(number[..styleLength]);
            }

            if (cell.Kind == XlsxCellKind.Text)
            {
                var text = XlsxText.Fit(cell.TextValue, out var cut);
                if (cut)
                {
                    TruncatedCells++;
                }

                _strings.Add(text).TryFormat(number, out var indexLength, default, CultureInfo.InvariantCulture);
                sheet.Write("\" t=\"s\"><v>");
                sheet.Write(number[..indexLength]);
            }
            else
            {
                // The shortest digits that read back as the same number.
                cell.NumberValue.TryFormat(number, out var numberLength, "R", CultureInfo.InvariantCulture);
                sheet.Write("\"><v>");
                sheet.Write(number[..numberLength]);
            }

            sheet.Write("</v></c>");
        }

        sheet.Write("</row>");
    }

    /// <summary>Ends the sheet that's open.</summary>
    public void EndSheet()
    {
        var sheet = _sheet ?? throw new InvalidOperationException("No sheet has been begun.");
        sheet.Write("</sheetData>");
        if (_filter && _rows > 0 && _widest > 0)
        {
            sheet.Write(string.Create(CultureInfo.InvariantCulture, $"<autoFilter ref=\"A1:{Letters(_widest - 1)}{_rows}\"/>"));
        }

        sheet.Write("</worksheet>");
        sheet.Dispose();
        _sheet = null;
    }

    /// <summary>Writes the rest of the workbook: its strings, its styles, and the parts that name the sheets. Nothing can be written after.</summary>
    public void Complete()
    {
        if (_sheet is not null)
        {
            EndSheet();
        }

        if (_sheets.Count == 0)
        {
            // A workbook needs a sheet.
            BeginSheet("Sheet1", []);
            EndSheet();
        }

        using (var part = _archive.CreateEntry("xl/sharedStrings.xml", _compression).Open())
        {
            _strings.CopyTo(part);
        }

        Part("xl/styles.xml", STYLES);

        var workbook = new StringBuilder(DECLARATION).Append("<workbook xmlns=\"" + MAIN + "\" xmlns:r=\"" + RELATIONSHIPS + "\"><bookViews><workbookView/></bookViews><sheets>");
        var relationships = new StringBuilder(DECLARATION).Append("<Relationships xmlns=\"" + PACKAGE_RELATIONSHIPS + "\">");
        var types = new StringBuilder(DECLARATION).Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
            .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
            .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
            .Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        using (var names = new StringWriter(CultureInfo.InvariantCulture))
        {
            for (var index = 0; index < _sheets.Count; index++)
            {
                var id = (index + 1).ToString(CultureInfo.InvariantCulture);
                names.GetStringBuilder().Clear();
                XlsxText.WriteAttribute(names, _sheets[index]);
                workbook.Append("<sheet name=\"").Append(names.GetStringBuilder()).Append("\" sheetId=\"").Append(id).Append("\" r:id=\"rId").Append(id).Append("\"/>");
                relationships.Append("<Relationship Id=\"rId").Append(id).Append("\" Type=\"" + RELATIONSHIPS + "/worksheet\" Target=\"worksheets/sheet").Append(id).Append(".xml\"/>");
                types.Append("<Override PartName=\"/xl/worksheets/sheet").Append(id)
                    .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
        }

        var next = _sheets.Count + 1;
        workbook.Append("</sheets></workbook>");
        relationships.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{next}\" Type=\"{RELATIONSHIPS}/styles\" Target=\"styles.xml\"/>")
            .Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{next + 1}\" Type=\"{RELATIONSHIPS}/sharedStrings\" Target=\"sharedStrings.xml\"/>")
            .Append("</Relationships>");
        types.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>")
            .Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>")
            .Append("</Types>");

        Part("xl/workbook.xml", workbook.ToString());
        Part("xl/_rels/workbook.xml.rels", relationships.ToString());
        Part("_rels/.rels",
            DECLARATION + "<Relationships xmlns=\"" + PACKAGE_RELATIONSHIPS + "\"><Relationship Id=\"rId1\" Type=\"" + RELATIONSHIPS +
            "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Part("[Content_Types].xml", types.ToString());

        // Writes the ZIP's directory: the file is whole once this returns.
        _completed = true;
        _archive.Dispose();
    }

    public void Dispose()
    {
        _sheet?.Dispose();
        _sheet = null;
        _strings.Dispose();
        if (!_completed)
        {
            _completed = true;
            _archive.Dispose();
        }
    }

    private void Part(string name, string xml)
    {
        using var part = _archive.CreateEntry(name, _compression).Open();
        part.Write(UTF8.GetBytes(xml));
    }

    /// <summary>A column's letters: A to Z, then AA and on.</summary>
    private string Letters(int column)
    {
        while (_letters.Count <= column)
        {
            var letters = string.Empty;
            for (var rest = _letters.Count + 1; rest > 0; rest = (rest - 1) / 26)
            {
                letters = (char)('A' + ((rest - 1) % 26)) + letters;
            }

            _letters.Add(letters);
        }

        return _letters[column];
    }

    /// <summary>A name cut to a length, ending "…" where it was cut.</summary>
    private static string Cut(string name, int length) => name.Length <= length ? name : name[..(length - 1)].TrimEnd() + "…";
}
