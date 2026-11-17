using System.Text;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Turns response bytes into text using the HTML encoding-sniffing order: byte order mark, then the
/// Content-Type charset, then a &lt;meta charset&gt; prescan of the first 1024 bytes, then UTF-8.
/// </summary>
internal static partial class CharsetDecoder
{
    /// <summary>How far into the bytes browsers look for a meta tag that declares the encoding.</summary>
    public const int PRESCAN_BYTES = 1024;

    static CharsetDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The label of UTF-8, as <see cref="ByteOrderMarkOf"/> gives it.</summary>
    public const string UTF_8 = "utf-8";

    /// <summary>The label of big-endian UTF-16, as <see cref="ByteOrderMarkOf"/> gives it.</summary>
    public const string UTF_16BE = "utf-16be";

    /// <summary>The label of little-endian UTF-16, as <see cref="ByteOrderMarkOf"/> gives it.</summary>
    public const string UTF_16LE = "utf-16le";

    public static string Decode(ReadOnlySpan<byte> bytes, string? headerCharset) => ByteOrderMarkOf(bytes) switch
    {
        UTF_8 => Encoding.UTF8.GetString(bytes[3..]),
        UTF_16BE => Encoding.BigEndianUnicode.GetString(bytes[2..]),
        UTF_16LE => Encoding.Unicode.GetString(bytes[2..]),
        _ => (Resolve(headerCharset) ?? Resolve(Prescan(bytes)) ?? Encoding.UTF8).GetString(bytes)
    };

    /// <summary>
    /// The encoding a byte-order mark at the start of the bytes declares, by its label (<see cref="UTF_8"/>,
    /// <see cref="UTF_16BE"/>, <see cref="UTF_16LE"/>: the three the HTML standard sniffs for), or null with none. The mark
    /// beats every other declaration, and <see cref="Decode"/> leaves it out of the text, so this is the only place it
    /// can be seen.
    /// </summary>
    public static string? ByteOrderMarkOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return UTF_8;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return UTF_16BE;
        }

        return bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? UTF_16LE : null;
    }

    /// <summary>
    /// How many bytes the text takes in the encoding the label names (UTF-8 for none, or one that can't be used), to
    /// say how far into a page's bytes a tag in its decoded text ends.
    /// </summary>
    public static int ByteCount(ReadOnlySpan<char> text, string? label) => (Resolve(label) ?? Encoding.UTF8).GetByteCount(text);

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
