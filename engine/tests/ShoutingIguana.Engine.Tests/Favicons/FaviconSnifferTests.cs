using System.Text;
using ShoutingIguana.Engine.Favicons;
using ShoutingIguana.Engine.Favicons.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Engine.Tests.Favicons;

/// <summary>
/// A site's icon is what its bytes say it is: an image of a kind a browser draws without running anything, with a size
/// its header gives, no bigger than an icon may be. Everything else a site can send in its place is not an image.
/// </summary>
public sealed class FaviconSnifferTests
{
    [Fact]
    public void EachKindOfImageIsToldByItsSignatureWithItsSize()
    {
        Assert.Equal(new FaviconImage("image/png", 192, 180), FaviconSniffer.Sniff(TestImages.Png(192, 180)));
        Assert.Equal(new FaviconImage("image/gif", 16, 17), FaviconSniffer.Sniff(TestImages.Gif(16, 17)));
        Assert.Equal(new FaviconImage("image/jpeg", 64, 48), FaviconSniffer.Sniff(TestImages.Jpeg(64, 48)));
        Assert.Equal(new FaviconImage("image/webp", 300, 200), FaviconSniffer.Sniff(TestImages.WebPExtended(300, 200)));
        Assert.Equal(new FaviconImage("image/webp", 128, 96), FaviconSniffer.Sniff(TestImages.WebPLossless(128, 96)));
        Assert.Equal(new FaviconImage("image/webp", 32, 33), FaviconSniffer.Sniff(TestImages.WebPLossy(32, 33)));

        // An icon file is as big as its largest image, a side of 0 standing for 256; its images are PNGs or bitmaps.
        Assert.Equal(new FaviconImage("image/x-icon", 48, 48), FaviconSniffer.Sniff(TestImages.Ico(false, (16, 16), (48, 48), (32, 32))));
        Assert.Equal(new FaviconImage("image/x-icon", 256, 256), FaviconSniffer.Sniff(TestImages.Ico(true, (256, 256), (16, 16))));

        // The synthetic site's own icon is a real PNG.
        Assert.Equal(new FaviconImage("image/png", 32, 32), FaviconSniffer.Sniff([.. SyntheticSite.Favicon]));
        Assert.Equal(["image/png", "image/x-icon", "image/gif", "image/jpeg", "image/webp"], FaviconSniffer.CONTENT_TYPES);
    }

    [Fact]
    public void WhatIsNotAnImageTheEngineKeepsIsNotOne()
    {
        (string What, byte[] Bytes)[] refused =
        [
            ("nothing", []),
            ("an SVG", TestImages.Svg()),
            ("an SVG with an XML declaration", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>")),
            ("a page", Encoding.UTF8.GetBytes("<!doctype html><html><script>alert(1)</script></html>")),
            ("a script", Encoding.UTF8.GetBytes("alert(1)")),
            ("a bitmap file", [(byte)'B', (byte)'M', .. new byte[60]]),
            ("a PNG behind a byte order mark", [0xEF, 0xBB, 0xBF, .. TestImages.Png(32, 32)]),
            ("a PNG behind a space", [0x20, .. TestImages.Png(32, 32)]),
            ("a PNG's signature and no header", TestImages.Png(32, 32)[..8]),
            ("a PNG whose first chunk isn't its header", Renamed(TestImages.Png(32, 32), 12, "tEXt")),
            ("a PNG of no width", TestImages.Png(0, 32)),
            ("a PNG too wide", TestImages.Png(FaviconSniffer.MAX_DIMENSION + 1, 32)),
            ("a PNG too tall", TestImages.Png(32, 100_000)),
            ("a PNG whose size is no number an image has", TestImages.Png(-1, -1)),
            ("a GIF cut short", TestImages.Gif(16, 16)[..8]),
            ("a GIF of no height", TestImages.Gif(16, 0)),
            ("a JPEG with no frame header", [0xFF, 0xD8, 0xFF, 0xD9, .. new byte[20]]),
            ("a JPEG too big", TestImages.Jpeg(4_000, 3_000)),
            ("a WebP of no kind known", Renamed(TestImages.WebPExtended(32, 32), 12, "ALPH")),
            ("a WebP too big", TestImages.WebPExtended(16_000, 16_000)),
            ("a RIFF file that isn't a WebP", Renamed(TestImages.WebPExtended(32, 32), 8, "WAVE")),
            ("a cursor file", Changed(TestImages.Ico(false, (32, 32)), 2, 2)),
            ("an icon file listing no image", Changed(TestImages.Ico(false, (32, 32)), 4, 0)),
            ("an icon file listing more images than it holds", Changed(TestImages.Ico(false, (32, 32)), 4, 9)),
            ("an icon file whose image lies past its end", Changed(TestImages.Ico(false, (32, 32)), 6 + 12, 200)),
            ("an icon file whose image is longer than the file", Changed(TestImages.Ico(false, (32, 32)), 6 + 8, 250)),
            ("an icon file whose image begins inside its directory", Changed(TestImages.Ico(false, (32, 32)), 6 + 12, 4)),
            ("an icon file whose image is neither a PNG nor a bitmap", Changed(TestImages.Ico(true, (32, 32)), 6 + 16, 41)),
            ("an icon file holding a page", [0x00, 0x00, 0x01, 0x00, .. Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")])
        ];
        Assert.All(refused, image => Assert.True(FaviconSniffer.Sniff(image.Bytes) is null, $"{image.What} was taken for an image."));

        // A header with nothing a browser could draw behind it, or with a page behind it, is no image either; nor is an
        // icon file whose image is bigger than its directory says, or than an icon may be.
        var png = TestImages.Png(32, 32);
        var header = png[..33];
        (string What, byte[] Bytes)[] hollow =
        [
            ("a PNG's header alone", header),
            ("a PNG's header and a page", [.. header, .. "<html><script>alert(1)</script></html>"u8.ToArray()]),
            ("a PNG with no image data", [.. header, .. TestImages.PngChunk("IEND", [])]),
            ("a PNG with no end", png[..^12]),
            ("a PNG whose header's checksum is wrong", Flipped(TestImages.Png(32, 32), 29)),
            ("a PNG whose data runs past its end", Changed(TestImages.Png(32, 32), 36, 0x7F)),
            ("a GIF with no trailer", TestImages.Gif(16, 16)[..^1]),
            ("a GIF with no image", [.. TestImages.Gif(16, 16)[..13], 0x3B]),
            ("a GIF whose image data runs past its end", [.. TestImages.Gif(16, 16)[..^3]]),
            ("a JPEG with no scan", [.. TestImages.Jpeg(64, 48)[..39], 0xFF, 0xD9]),
            ("a JPEG whose scan has no end", TestImages.Jpeg(64, 48)[..^2]),
            ("a WebP longer than its file", Changed(TestImages.WebPLossless(32, 32), 5, 0x10)),
            ("a WebP whose chunk runs past it", Changed(TestImages.WebPLossless(32, 32), 17, 0x10)),
            ("an extended WebP with no image", Fixed(TestImages.WebPExtended(32, 32)[..30])),
            ("an icon file holding a huge PNG", IcoHolding(TestImages.Png(60_000, 60_000), 16, 16)),
            ("an icon file holding a PNG of another size", IcoHolding(TestImages.Png(64, 64), 16, 16)),
            ("an icon file holding a PNG's header alone", IcoHolding(header, 32, 32)),
            ("an icon file saying 256 of a 128 PNG", IcoHolding(TestImages.Png(128, 128), 0, 0)),
            ("an icon file holding a bitmap of another size", IcoHolding(TestImages.Bitmap(64, 64), 32, 32)),
            ("an icon file holding a bitmap with too few pixels", IcoHolding(TestImages.Bitmap(32, 32)[..1_000], 32, 32)),
            ("an icon file holding a bitmap too big", IcoHolding(Changed(Changed(TestImages.Bitmap(4, 4), 5, 0x40), 9, 0x40), 0, 0))
        ];
        Assert.All(hollow, image => Assert.True(FaviconSniffer.Sniff(image.Bytes) is null, $"{image.What} was taken for an image."));

        // While what holds up as an image, its data whole, is one.
        Assert.Equal(new FaviconImage("image/x-icon", 512, 512), FaviconSniffer.Sniff(IcoHolding(TestImages.Png(512, 512), 0, 0)));
        Assert.Equal(new FaviconImage("image/x-icon", 32, 32), FaviconSniffer.Sniff(IcoHolding(TestImages.Bitmap(32, 32), 32, 32)));
        Assert.Equal(new FaviconImage("image/png", 32, 32), FaviconSniffer.Sniff([.. png, .. "trailing bytes after the end"u8.ToArray()]));

        // An image of the most bytes an icon may be is one; a byte more and it isn't.
        Assert.NotNull(FaviconSniffer.Sniff(TestImages.Png(64, 64, FaviconSniffer.MAX_BYTES)));
        Assert.Null(FaviconSniffer.Sniff(TestImages.Png(64, 64, FaviconSniffer.MAX_BYTES + 1)));
        Assert.NotNull(FaviconSniffer.Sniff(TestImages.Png(FaviconSniffer.MAX_DIMENSION, FaviconSniffer.MAX_DIMENSION)));
    }

    private static byte[] Renamed(byte[] image, int at, string name)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(image, at);
        return image;
    }

    private static byte[] Changed(byte[] image, int at, byte value)
    {
        image[at] = value;
        return image;
    }

    private static byte[] Flipped(byte[] image, int at)
    {
        image[at] ^= 0xFF;
        return image;
    }

    /// <summary>A RIFF file cut short, its length said again to be what's left: what's in it is all there is.</summary>
    private static byte[] Fixed(byte[] riff)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(riff.Length - 8));
        return riff;
    }

    /// <summary>An icon file holding one image, whatever it is, under the size its directory gives.</summary>
    private static byte[] IcoHolding(byte[] image, byte width, byte height)
    {
        var ico = new byte[6 + 16 + image.Length];
        (ico[2], ico[4], ico[6], ico[7]) = (1, 1, width, height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 8), (uint)image.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12), 22);
        image.CopyTo(ico, 22);
        return ico;
    }
}
