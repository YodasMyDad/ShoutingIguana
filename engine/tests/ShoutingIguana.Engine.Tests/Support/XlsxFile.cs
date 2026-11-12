using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// A workbook read back with the Open XML SDK, which is Microsoft's own reading of the format and no part of the engine:
/// each sheet's cells as text or numbers, and whether the file is valid against the format's schema, which is what
/// Excel repairs a file for not being.
/// </summary>
internal sealed partial class XlsxFile
{
    private XlsxFile(IReadOnlyList<Sheet> sheets) => Sheets = sheets;

    public IReadOnlyList<Sheet> Sheets { get; }

    /// <summary>One sheet: its name, its rows, and how it's laid out.</summary>
    /// <param name="Rows">Each row's cells by column, a string, a double (a date is one, with <see cref="XlsxCellInfo.Style"/> 3) or null for no cell.</param>
    /// <param name="FrozenRows">The rows that stay in view.</param>
    /// <param name="Filter">The range its filter covers, or null.</param>
    internal sealed record Sheet(string Name, IReadOnlyList<IReadOnlyList<XlsxCellInfo>> Rows, int FrozenRows, string? Filter, IReadOnlyList<double> Widths)
    {
        /// <summary>The rows' values alone, each row as wide as the widest: a file writes no cell for a value that isn't there.</summary>
        public List<object?[]> Values
        {
            get
            {
                var width = Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);
                return [.. Rows.Select(row => Enumerable.Range(0, width).Select(column => column < row.Count ? row[column].Value : null).ToArray())];
            }
        }
    }

    /// <summary>A cell: its value, the index of its style, and whether the file holds it as text.</summary>
    internal readonly record struct XlsxCellInfo(object? Value, uint Style, bool IsText);

    /// <summary>Reads the workbook, having checked it against the schema: a file Excel would repair fails here.</summary>
    public static XlsxFile Read(string path)
    {
        AssertValid(path);
        using var document = SpreadsheetDocument.Open(path, isEditable: false);
        var workbook = document.WorkbookPart!;
        var strings = workbook.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(item => Unescape(item.InnerText)).ToList() ?? [];
        List<Sheet> sheets = [];
        foreach (var sheet in workbook.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var worksheet = part.Worksheet;
            List<IReadOnlyList<XlsxCellInfo>> rows = [];
            uint expectedRow = 1;
            foreach (var row in worksheet.GetFirstChild<SheetData>()!.Elements<Row>())
            {
                Assert.Equal(expectedRow++, row.RowIndex!.Value);
                List<XlsxCellInfo> cells = [];
                foreach (var cell in row.Elements<Cell>())
                {
                    var (column, rowNumber) = Place(cell.CellReference!.Value!);
                    Assert.Equal(row.RowIndex.Value, rowNumber);
                    while (cells.Count < column)
                    {
                        cells.Add(default);
                    }

                    var text = cell.CellValue?.Text;
                    var isText = cell.DataType?.Value == CellValues.SharedString;
                    Assert.True(cell.DataType is null || isText, $"Cell {cell.CellReference.Value} is of a type the exports don't write: {cell.DataType}.");
                    Assert.Null(cell.CellFormula);
                    object? value = isText ? strings[int.Parse(text!, CultureInfo.InvariantCulture)] : double.Parse(text!, NumberStyles.Float, CultureInfo.InvariantCulture);
                    cells.Add(new XlsxCellInfo(value, cell.StyleIndex?.Value ?? 0, isText));
                }

                rows.Add(cells);
            }

            var pane = worksheet.GetFirstChild<SheetViews>()?.GetFirstChild<SheetView>()?.GetFirstChild<Pane>();
            var frozen = pane?.State?.Value == PaneStateValues.Frozen ? (int)(pane.VerticalSplit?.Value ?? 0) : 0;
            var widths = worksheet.GetFirstChild<Columns>()?.Elements<Column>().Select(column => column.Width!.Value).ToList() ?? [];
            sheets.Add(new Sheet(sheet.Name!.Value!, rows, frozen, worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value, widths));
        }

        return new XlsxFile(sheets);
    }

    /// <summary>
    /// Each sheet's name and its rows' first cells as written (a number's digits, or a shared string's index), read a row
    /// at a time: for a sheet of a million rows, which <see cref="Read"/> would hold all of.
    /// </summary>
    public static List<(string Name, List<string?> FirstCells)> Scan(string path)
    {
        using var document = SpreadsheetDocument.Open(path, isEditable: false);
        var workbook = document.WorkbookPart!;
        List<(string, List<string?>)> sheets = [];
        foreach (var sheet in workbook.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            List<string?> firsts = [];
            using var reader = OpenXmlReader.Create(workbook.GetPartById(sheet.Id!.Value!));
            while (reader.Read())
            {
                if (reader.ElementType == typeof(Row) && reader.IsStartElement)
                {
                    var row = (Row)reader.LoadCurrentElement()!;
                    firsts.Add(row.GetFirstChild<Cell>()?.CellValue?.Text);
                }
            }

            sheets.Add((sheet.Name!.Value!, firsts));
        }

        return sheets;
    }

    /// <summary>Fails when the file isn't valid against the format's schema, saying how.</summary>
    public static void AssertValid(string path)
    {
        using var document = SpreadsheetDocument.Open(path, isEditable: false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2010).Validate(document).Take(20)
            .Select(error => $"{error.Part?.Uri}: {error.Description} at {error.Path?.XPath}").ToList();
        Assert.True(errors.Count == 0, $"The workbook isn't valid:\n{string.Join("\n", errors)}");
    }

    /// <summary>"C12" as a column from 0 and a row from 1.</summary>
    private static (int Column, uint Row) Place(string reference)
    {
        var letters = reference.TakeWhile(char.IsAsciiLetterUpper).ToArray();
        var column = letters.Aggregate(0, (sum, letter) => sum * 26 + (letter - 'A' + 1)) - 1;
        return (column, uint.Parse(reference.AsSpan(letters.Length), CultureInfo.InvariantCulture));
    }

    /// <summary>A shared string as the text it stands for: SpreadsheetML writes a character XML can't hold as <c>_xHHHH_</c>.</summary>
    private static string Unescape(string text) =>
        Escaped().Replace(text, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    [GeneratedRegex("_x([0-9A-Fa-f]{4})_")]
    private static partial Regex Escaped();
}
