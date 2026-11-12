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
}
