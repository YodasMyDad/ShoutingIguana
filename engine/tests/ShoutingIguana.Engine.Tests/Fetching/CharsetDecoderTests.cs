using System.Text;
using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class CharsetDecoderTests
{
    [Fact]
    public void ByteOrderMarkWins() =>
        Assert.Equal("é", CharsetDecoder.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9], "windows-1252"));

    [Fact]
    public void HeaderCharsetIsUsed() => Assert.Equal("é", CharsetDecoder.Decode([0xE9], "windows-1252"));

    [Fact]
    public void MetaCharsetIsFoundByPrescan()
    {
        var bytes = Encoding.ASCII.GetBytes("<html><head><meta charset=\"iso-8859-1\"></head><body>").Concat(new byte[] { 0xE9 }).ToArray();

        Assert.EndsWith("é", CharsetDecoder.Decode(bytes, null));
    }

    [Fact]
    public void DefaultsToUtf8() => Assert.Equal("é", CharsetDecoder.Decode([0xC3, 0xA9], null));

    [Fact]
    public void UnknownLabelsFallBack() => Assert.Equal("é", CharsetDecoder.Decode([0xC3, 0xA9], "x-made-up"));
}
