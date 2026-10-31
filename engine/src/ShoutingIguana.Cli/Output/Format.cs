using System.Globalization;
using System.Text;

namespace ShoutingIguana.Cli.Output;

internal static class Format
{
    private static readonly string[] UNITS = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < UNITS.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {UNITS[unit]}");
    }

    public static double Megabytes(long bytes) => Math.Round(bytes / (1024d * 1024d), 1);

    /// <summary>"1 page", "2 pages", "1,204 pages".</summary>
    public static string Count(long count, string one, string many) => $"{count:N0} {(count == 1 ? one : many)}";

    /// <summary>mm:ss, or h:mm:ss with as many hours as it takes (a two-day crawl shows 49:12:03).</summary>
    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalHours}:{duration:mm\\:ss}")
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Text from crawled pages made safe for a terminal: control characters (which can move the cursor, retitle the
    /// window or hide text) and bidirectional overrides (which can make text read differently) are shown escaped.
    /// </summary>
    public static string Printable(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(IsUnsafe))
        {
            return text ?? string.Empty;
        }

        var printable = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            if (IsUnsafe(character))
            {
                printable.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:X4}");
            }
            else
            {
                printable.Append(character);
            }
        }

        return printable.ToString();
    }

    private static bool IsUnsafe(char character) =>
        char.IsControl(character) || (int)character is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);
}
