using System.Text;
using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class CharsetDecoderTests
{
    private const string WINDOWS_1252_TEXT = "€“”";

    /// <summary>0x80, 0x93 and 0x94 are €, “ and ” in windows-1252 but C1 control characters in ISO-8859-1.</summary>
    private static readonly byte[] WINDOWS_1252_ONLY = [0x80, 0x93, 0x94];

    [Fact]
    public void ByteOrderMarkWins() =>
        Assert.Equal("é", CharsetDecoder.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9], "windows-1252"));

    [Fact]
    public void Utf16ByteOrderMarksWin()
    {
        Assert.Equal("hi", CharsetDecoder.Decode([0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0], "windows-1252"));
        Assert.Equal("hi", CharsetDecoder.Decode([0xFE, 0xFF, 0, (byte)'h', 0, (byte)'i'], "windows-1252"));
    }

    /// <summary>
    /// The decoded text has no mark left, so which one the bytes began with is asked for apart: the three the HTML
    /// standard looks for, by the encoding each declares, and nothing for bytes that begin any other way.
    /// </summary>
    [Fact]
    public void AByteOrderMarkIsNamedByTheEncodingItDeclares()
    {
        Assert.Equal("utf-8", CharsetDecoder.ByteOrderMarkOf([0xEF, 0xBB, 0xBF, (byte)'<']));
        Assert.Equal("utf-16le", CharsetDecoder.ByteOrderMarkOf([0xFF, 0xFE, (byte)'<', 0]));
        Assert.Equal("utf-16be", CharsetDecoder.ByteOrderMarkOf([0xFE, 0xFF, 0, (byte)'<']));

        Assert.Null(CharsetDecoder.ByteOrderMarkOf("<!DOCTYPE html>"u8));
        Assert.Null(CharsetDecoder.ByteOrderMarkOf([0xEF, 0xBB]));
        Assert.Null(CharsetDecoder.ByteOrderMarkOf([]));

        // The mark itself is no part of the text.
        Assert.Equal("<p>", CharsetDecoder.Decode([0xEF, 0xBB, 0xBF, (byte)'<', (byte)'p', (byte)'>'], null));
    }

    [Fact]
    public void HeaderCharsetIsUsed() => Assert.Equal("é", CharsetDecoder.Decode([0xE9], "windows-1252"));

    [Fact]
    public void HeaderCharsetBeatsMetaCharset()
    {
        var bytes = Html("<meta charset=\"utf-8\">", [0xC3, 0xA9]);

        Assert.EndsWith("Ã©", CharsetDecoder.Decode(bytes, "windows-1252"));
    }

    [Fact]
    public void MetaCharsetIsFoundByPrescanAndLatin1MeansWindows1252()
    {
        var bytes = Html("<meta charset=\"iso-8859-1\">", WINDOWS_1252_ONLY);

        Assert.EndsWith(WINDOWS_1252_TEXT, CharsetDecoder.Decode(bytes, null));
    }

    [Fact]
    public void DefaultsToUtf8() => Assert.Equal("é", CharsetDecoder.Decode([0xC3, 0xA9], null));

    [Fact]
    public void UnknownHeaderLabelFallsThroughToTheMetaCharset()
    {
        var bytes = Html("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\">", WINDOWS_1252_ONLY);

        Assert.EndsWith(WINDOWS_1252_TEXT, CharsetDecoder.Decode(bytes, "x-made-up"));
    }

    [Fact]
    public void UnknownLabelsEverywhereFallBackToUtf8() =>
        Assert.EndsWith("é", CharsetDecoder.Decode(Html("<meta charset=\"x-also-made-up\">", [0xC3, 0xA9]), "x-made-up"));

    private static byte[] Html(string head, byte[] body) =>
        [.. Encoding.ASCII.GetBytes($"<html><head>{head}</head><body>"), .. body];

    /// <summary>
    /// A meta tag that names UTF-16 by any of its labels was read as bytes, so the page is not UTF-16: the encoding
    /// standard reads it as UTF-8. berkshirehathaway.com says charset=unicode, and was stored as one page of garbage.
    /// </summary>
    [Theory]
    [InlineData("unicode")]
    [InlineData("Unicode")]
    [InlineData("utf-16")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    [InlineData("ucs-2")]
    [InlineData("iso-10646-ucs-2")]
    [InlineData("csunicode")]
    [InlineData("unicodefeff")]
    [InlineData("unicodefffe")]
    [InlineData("utf-32")]
    [InlineData("utf-7")]
    public void AMetaTagThatNamesUtf16IsReadAsUtf8(string label)
    {
        var html = $"<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset={label}\"><title>Café owners</title></head><body><h1>Hello</h1></body></html>";
        var decoded = CharsetDecoder.Decode(System.Text.Encoding.UTF8.GetBytes(html), null);
        Assert.Equal(html, decoded);

        // The same label in the Content-Type header, on bytes with no byte order mark, is the same mistake.
        Assert.Equal(html, CharsetDecoder.Decode(System.Text.Encoding.UTF8.GetBytes(html), label));
    }

    [Fact]
    public void AByteOrderMarkStillWinsOverAMetaTagThatNamesUtf16()
    {
        const string HTML = "<html><head><meta charset=\"unicode\"><title>T</title></head></html>";
        byte[] bytes = [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes(HTML)];
        Assert.Equal(HTML, CharsetDecoder.Decode(bytes, null));
    }
}
