using System.Text;
using ShoutingIguana.Engine.Export.Tables;

namespace ShoutingIguana.Cli.Output;

/// <summary>
/// RFC 4180 CSV: fields with commas, quotes or line breaks are quoted, and quotes are doubled. Details come from the
/// crawled pages, so a field that a spreadsheet would run as a formula gets a leading apostrophe, and one longer than
/// Excel reads as one cell is cut to that (<see cref="CsvTableWriter.MAX_FIELD_CHARS"/>, as <c>si export</c> cuts it):
/// Excel would end the row there and start another with the rest, unguarded.
/// </summary>
internal sealed class CsvWriter(TextWriter writer)
{
    /// <summary>The fields cut to what Excel reads as one cell.</summary>
    public long TruncatedFields { get; private set; }

    private static readonly char[] SPECIAL = [',', '"', '\r', '\n'];
    private static readonly char[] FORMULA_STARTS = ['=', '+', '-', '@', '\t', '\r'];

    public static CsvWriter Create(string path) => new(new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

    public void WriteRow(params string?[] fields)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0)
            {
                writer.Write(',');
            }

            var field = fields[index] ?? string.Empty;
            if (field.Length > 0 && FORMULA_STARTS.Contains(field[0]))
            {
                field = "'" + field;
            }

            if (field.Length > CsvTableWriter.MAX_FIELD_CHARS)
            {
                // Never between the two halves of a pair.
                field = field[..(char.IsHighSurrogate(field[CsvTableWriter.MAX_FIELD_CHARS - 1]) ? CsvTableWriter.MAX_FIELD_CHARS - 1 : CsvTableWriter.MAX_FIELD_CHARS)];
                TruncatedFields++;
            }

            writer.Write(field.IndexOfAny(SPECIAL) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field);
        }

        writer.Write("\r\n");
    }

    public void Close() => writer.Dispose();
}
