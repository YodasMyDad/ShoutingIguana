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
}
