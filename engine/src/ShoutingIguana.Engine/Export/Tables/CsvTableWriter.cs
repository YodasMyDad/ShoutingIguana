using System.Globalization;
using System.Text;

namespace ShoutingIguana.Engine.Export.Tables;

/// <summary>
/// RFC 4180 CSV as Excel reads it: UTF-8 with a byte-order mark (without one Excel takes the file for the system's code
/// page), rows ended CRLF, a field with a comma, a quote or a line break in quotes, with its quotes doubled.
/// <para>
/// Text is the crawled site's, and a spreadsheet runs a field that starts with =, +, - or @ as a formula. Such text, and
/// text that starts with a tab or a carriage return, is written with an apostrophe before it, which a spreadsheet shows
/// as plain text (OWASP's guard against CSV injection). A number is written as its digits, so a negative one in a
/// numeric column stays the number it is.
/// </para>
/// <para>
/// <b>No field is longer than Excel reads as one cell</b> (<see cref="MAX_FIELD_CHARS"/>): Excel ends the row where a
/// longer field passes that, and starts another with what is left, in column A, where no apostrophe guards it. A site's
/// URL has no limit on its length, so a formula at the end of a long one would run. Longer text is cut, and counted
/// (<see cref="TruncatedCells"/>), as a workbook's is.
/// </para>
/// </summary>
internal sealed class CsvTableWriter : ITableWriter
{
    /// <summary>
    /// The most characters of a CSV field that Excel reads as one cell: 32,758, nine short of the 32,767 a cell holds.
    /// Found by opening fields of every length around it in Excel 16: one of 32,758 characters is read whole and the
    /// row goes on; at 32,759 the row ends there and the rest of the line starts a new row. The count is of the cell's
    /// own text in UTF-16 units, whatever the alphabet and the column: the quotes around a field and the ones doubled
    /// inside it are the file's and don't count, and the apostrophe that guards a formula is the cell's and does.
    /// </summary>
    public const int MAX_FIELD_CHARS = 32_758;

    private const int BUFFER_BYTES = 1 << 16;
    private static readonly System.Buffers.SearchValues<char> SPECIAL = System.Buffers.SearchValues.Create(",\"\r\n");

    private readonly StreamWriter _writer;
    private bool _begun;
    private bool _disposed;

    /// <param name="output">Where the file goes; left open.</param>
    public CsvTableWriter(Stream output) =>
        _writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), BUFFER_BYTES, leaveOpen: true) { NewLine = "\r\n" };

    public int Sheets => 0;

    public long TruncatedCells { get; private set; }

    public void Begin(string name, IReadOnlyList<string> titles)
    {
        if (_begun)
        {
            throw new InvalidOperationException("A CSV file holds one table.");
        }

        _begun = true;
        for (var index = 0; index < titles.Count; index++)
        {
            if (index > 0)
            {
                _writer.Write(',');
            }

            WriteText(titles[index]);
        }

        _writer.Write("\r\n");
    }

    public void WriteRow(ReadOnlySpan<object?> cells)
    {
        Span<char> number = stackalloc char[40];
        for (var index = 0; index < cells.Length; index++)
        {
            if (index > 0)
            {
                _writer.Write(',');
            }

            switch (cells[index])
            {
                case null:
                    break;
                case string text:
                    WriteText(text);
                    break;
                case long whole:
                    whole.TryFormat(number, out var wholeLength, default, CultureInfo.InvariantCulture);
                    _writer.Write(number[..wholeLength]);
                    break;
                case int whole:
                    whole.TryFormat(number, out var intLength, default, CultureInfo.InvariantCulture);
                    _writer.Write(number[..intLength]);
                    break;
                case double real when double.IsFinite(real):
                    real.TryFormat(number, out var realLength, "R", CultureInfo.InvariantCulture);
                    _writer.Write(number[..realLength]);
                    break;
                case double:
                    break;
                case bool flag:
                    _writer.Write(flag ? Storage.ColumnWords.YES : Storage.ColumnWords.NO);
                    break;
                case DateTime moment:
                    // No comma and no quote: read as a date by a spreadsheet, and as it is by anything else.
                    _writer.Write(moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteText(Convert.ToString(cells[index], CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
            }
        }

        _writer.Write("\r\n");
    }

    public void End()
    {
    }

    public void Complete() => _writer.Flush();

    public void Dispose()
    {
        // A writer that leaves its stream open flushes every time it's disposed: once is enough, as the stream may be closed by then.
        if (!_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }

    private void WriteText(ReadOnlySpan<char> text)
    {
        var guarded = SpreadsheetText.LooksLikeFormula(text);

        // Cut to what Excel reads as one cell, the guard's apostrophe among it, and never between the halves of a pair.
        var most = guarded ? MAX_FIELD_CHARS - 1 : MAX_FIELD_CHARS;
        if (text.Length > most)
        {
            text = text[..(char.IsHighSurrogate(text[most - 1]) ? most - 1 : most)];
            TruncatedCells++;
        }

        if (!guarded && !text.ContainsAny(SPECIAL))
        {
            _writer.Write(text);
            return;
        }

        var quoted = text.ContainsAny(SPECIAL);
        if (quoted)
        {
            _writer.Write('"');
        }

        if (guarded)
        {
            _writer.Write('\'');
        }

        if (quoted)
        {
            while (text.IndexOf('"') is var quote and >= 0)
            {
                _writer.Write(text[..(quote + 1)]);
                _writer.Write('"');
                text = text[(quote + 1)..];
            }
        }

        _writer.Write(text);
        if (quoted)
        {
            _writer.Write('"');
        }
    }
}
