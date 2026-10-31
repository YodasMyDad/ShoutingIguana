using System.Text;

namespace ShoutingIguana.Cli.Output;

/// <summary>RFC 4180 CSV: fields with commas, quotes or line breaks are quoted, and quotes are doubled.</summary>
internal sealed class CsvWriter(TextWriter writer)
{
    private static readonly char[] SPECIAL = [',', '"', '\r', '\n'];

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
            writer.Write(field.IndexOfAny(SPECIAL) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field);
        }

        writer.Write("\r\n");
    }

    public void Close() => writer.Dispose();
}
