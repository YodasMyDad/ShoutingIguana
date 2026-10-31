using System.Globalization;

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

    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
