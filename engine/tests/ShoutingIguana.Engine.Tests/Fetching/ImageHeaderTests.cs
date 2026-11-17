using System.Buffers.Binary;
using System.Text;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>An image's size in pixels from the first bytes of its file, as the fetcher reads them, a piece at a time.</summary>
public sealed class ImageHeaderTests
{
    public static TheoryData<string, byte[], int, int> Images => new()
    {
        { "PNG", TestImages.Png(1200, 800), 1200, 800 },
        { "GIF", TestImages.Gif(320, 240), 320, 240 },
        { "JPEG", TestImages.Jpeg(4000, 3000), 4000, 3000 },
        { "WebP, lossy", TestImages.WebPLossy(640, 360), 640, 360 },
        { "WebP, lossless", TestImages.WebPLossless(500, 300), 500, 300 },
        { "WebP, extended", TestImages.WebPExtended(2048, 1536), 2048, 1536 },
        { "AVIF", Avif(1920, 1080, thumbnail: (160, 90)), 1920, 1080 }
    };

    [Theory]
    [MemberData(nameof(Images))]
    public void EachFormatSaysItsSize(string format, byte[] file, int width, int height)
    {
        Assert.Equal(ImageHeader.Result.Found, ImageHeader.TryRead(file, out var readWidth, out var readHeight));
        Assert.Equal((width, height), (readWidth, readHeight));
        Assert.NotNull(format);
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void TooFewBytesAskForMoreAndNeverGuess(string format, byte[] file, int width, int height)
    {
        // However the file arrives, every prefix short of the size says to read on, and the first that has it gives it.
        var found = false;
        for (var length = 0; length <= file.Length && !found; length++)
        {
            var result = ImageHeader.TryRead(file.AsSpan(0, length), out var readWidth, out var readHeight);
            Assert.NotEqual(ImageHeader.Result.Unknown, result);
            if (result == ImageHeader.Result.Found)
            {
                Assert.Equal((width, height), (readWidth, readHeight));
                found = true;
            }
        }

        Assert.True(found, format);
    }

    [Fact]
    public void AJpegsFrameHeaderIsFoundPastALargeExifBlockAndPadding()
    {
        // A camera's EXIF block of 60 KB, a colour profile, and fill bytes before a marker, then a progressive frame (SOF2).
        var exif = Segment(0xE1, 60_000);
        var profile = Segment(0xE2, 3_000);
        byte[] frame = [0xFF, 0xFF, 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x0B, 0xB8, 0x0F, 0xA0, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01];
        byte[] file = [0xFF, 0xD8, .. exif, .. profile, .. frame, 0xFF, 0xD9];

        Assert.Equal(ImageHeader.MAX_JPEG_BYTES, ImageHeader.MostToRead(file));
        Assert.Equal(ImageHeader.Result.NeedMore, ImageHeader.TryRead(file.AsSpan(0, 40_000), out _, out _));
        Assert.Equal(ImageHeader.Result.Found, ImageHeader.TryRead(file, out var width, out var height));
        Assert.Equal((4000, 3000), (width, height));
    }

    [Fact]
    public void AJpegsHuffmanTablesAreNotItsFrame()
    {
        // DHT (0xC4) shares the start-of-frame range and has no size in it.
        byte[] file = [0xFF, 0xD8, .. Segment(0xC4, 20), .. TestImages.Jpeg(30, 20)[20..]];

        Assert.Equal(ImageHeader.Result.Found, ImageHeader.TryRead(file, out var width, out var height));
        Assert.Equal((30, 20), (width, height));
    }

    [Fact]
    public void WhatHasNoSizeInItsHeaderOrIsDamagedHasNone()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>"u8.ToArray();
        byte[] scanBeforeFrame = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x0C, .. new byte[20]];
        byte[] notAMarker = [0xFF, 0xD8, 0x12, 0x34, .. new byte[20]];
        var png = TestImages.Png(10, 10);
        png[12] = (byte)'X';
        byte[] zeroWide = TestImages.Gif(0, 10);

        foreach (var file in new[] { svg, scanBeforeFrame, notAMarker, png, zeroWide, new byte[64] })
        {
            Assert.Equal(ImageHeader.Result.Unknown, ImageHeader.TryRead(file, out _, out _));
        }
    }

    /// <summary>A JPEG segment with this marker and this many bytes of data.</summary>
    private static byte[] Segment(byte marker, int data)
    {
        var segment = new byte[4 + data];
        (segment[0], segment[1]) = (0xFF, marker);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(data + 2));
        return segment;
    }

    /// <summary>The start of an AVIF: its file type box, then a meta box whose properties hold a thumbnail's extents and the image's.</summary>
    private static byte[] Avif(int width, int height, (int Width, int Height) thumbnail)
    {
        static byte[] Box(string type, byte[] body)
        {
            var box = new byte[8 + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
            body.CopyTo(box, 8);
            return box;
        }

        static byte[] Extents(int width, int height)
        {
            var body = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(8), (uint)height);
            return Box("ispe", body);
        }

        var fileType = Box("ftyp", [.. "avif"u8, 0, 0, 0, 0, .. "mif1"u8, .. "avif"u8]);
        var properties = Box("ipco", [.. Extents(thumbnail.Width, thumbnail.Height), .. Extents(width, height)]);
        return [.. fileType, .. Box("meta", [0, 0, 0, 0, .. Box("iprp", properties)]), .. new byte[32]];
    }
}
