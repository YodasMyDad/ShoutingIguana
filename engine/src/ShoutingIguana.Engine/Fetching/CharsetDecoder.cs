using System.Text;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Turns response bytes into text using the HTML encoding-sniffing order: byte order mark, then the
/// Content-Type charset, then a &lt;meta charset&gt; prescan of the first 1024 bytes, then UTF-8.
/// </summary>
internal static partial class CharsetDecoder
{
    private const int PRESCAN_BYTES = 1024;

    static CharsetDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(ReadOnlySpan<byte> bytes, string? headerCharset)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        var encoding = Resolve(headerCharset) ?? Resolve(Prescan(bytes)) ?? Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    private static string? Prescan(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, PRESCAN_BYTES)]);
        var match = MetaCharsetRegex().Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Encoding? Resolve(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var normalized = label.Trim().Trim('"', '\'').ToLowerInvariant();

        // The HTML standard decodes these labels as windows-1252, and a UTF-16 label declared inside
        // byte content can only be a mistake, so it is read as UTF-8.
        if (normalized is "iso-8859-1" or "iso8859-1" or "latin1" or "l1" or "us-ascii" or "ascii")
        {
            return Encoding.GetEncoding(1252);
        }

        if (normalized.StartsWith("utf-16", StringComparison.Ordinal))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(normalized);
        }
        catch (ArgumentException)
        {
            // Unknown label from the server; fall through to the next sniffing step.
            return null;
        }
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([a-zA-Z0-9_\-:.]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetRegex();
}
