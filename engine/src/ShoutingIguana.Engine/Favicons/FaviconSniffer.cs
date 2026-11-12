using System.Buffers.Binary;
using System.IO.Hashing;
using ShoutingIguana.Engine.Favicons.Models;

namespace ShoutingIguana.Engine.Favicons;

/// <summary>
/// Says what a site's icon is by its bytes, never by what the site calls it: a site can serve a page, a script or an
/// SVG (which can carry script) under any name and any Content-Type. Only raster images a browser draws without running
/// anything are images here: PNG, ICO, GIF, JPEG and WebP, each told by its signature, its size read from its header,
/// and its structure walked to the end of its image data, so a header with nothing after it, or followed by a page, is
/// not an image. Anything else is not an image, and neither is one whose size can't be read or is over
/// <see cref="MAX_DIMENSION"/> pixels a side, an icon file's images included: a few hundred kilobytes can declare a
/// picture that takes gigabytes to draw. The pixels themselves aren't decoded, so an image whose compressed data is
/// damaged can still be kept; the app then shows a broken image, and nothing in it can run.
/// </summary>
internal static class FaviconSniffer
{
    /// <summary>The most bytes an icon may be: one over is not kept. A favicon.ico with a 256 px image uncompressed is about 370 KB.</summary>
    public const int MAX_BYTES = 512 * 1024;

    /// <summary>The largest side, in pixels, of an image kept as an icon.</summary>
    public const int MAX_DIMENSION = 1_024;

    public const string PNG = "image/png";
    public const string ICO = "image/x-icon";
    public const string GIF = "image/gif";
    public const string JPEG = "image/jpeg";
    public const string WEBP = "image/webp";

    /// <summary>Every media type an icon is served as.</summary>
    public static readonly IReadOnlyList<string> CONTENT_TYPES = [PNG, ICO, GIF, JPEG, WEBP];

    private const int ICO_HEADER_BYTES = 6;
    private const int ICO_ENTRY_BYTES = 16;
    private const int BITMAP_HEADER_BYTES = 40;
    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The image the bytes are, or null when they aren't one the engine keeps.</summary>
    public static FaviconImage? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MAX_BYTES)
        {
            return null;
        }

        var size = Png(bytes) ?? Gif(bytes) ?? Jpeg(bytes) ?? WebP(bytes) ?? Ico(bytes);
        return size is { } image && Fits(image.Width, image.Height) ? new FaviconImage(image.ContentType, image.Width, image.Height) : null;
    }

    private static bool Fits(int width, int height) => width is >= 1 and <= MAX_DIMENSION && height is >= 1 and <= MAX_DIMENSION;

    /// <summary>
    /// The 8-byte signature, then chunks, each its length, its name, its data and a checksum that holds: the header
    /// (IHDR, 13 bytes, first), at least one chunk of image data (IDAT), and the end (IEND), after which nothing is read.
    /// </summary>
    private static (string ContentType, int Width, int Height)? Png(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(PNG_SIGNATURE))
        {
            return null;
        }

        (int Width, int Height)? header = null;
        var hasData = false;
        for (var at = 8; at + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);
            if (length > bytes.Length - at - 12)
            {
                return null;
            }

            var type = bytes.Slice(at + 4, 4);
            var data = bytes.Slice(at + 8, (int)length);
            if (Crc32.HashToUInt32(bytes.Slice(at + 4, 4 + (int)length)) != BinaryPrimitives.ReadUInt32BigEndian(bytes[(at + 8 + (int)length)..]))
            {
                return null;
            }

            if (header is null)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13)
                {
                    return null;
                }

                header = Sized(BinaryPrimitives.ReadUInt32BigEndian(data), BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                hasData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                return hasData ? (PNG, header.Value.Width, header.Value.Height) : null;
            }

            at += 12 + (int)length;
        }

        return null;
    }

    /// <summary>
    /// The signature and the screen's size, then blocks to the trailer (0x3B): extensions, and at least one image (its
    /// descriptor, its colour table, then its data), each block's data in sub-blocks that end with an empty one.
    /// </summary>
    private static (string ContentType, int Width, int Height)? Gif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 13 || !(bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        var (width, height) = (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        var at = 13 + ColourTable(bytes[10]);
        var images = 0;
        while (at < bytes.Length)
        {
            switch (bytes[at])
            {
                case 0x3B:
                    return images > 0 ? (GIF, width, height) : null;
                case 0x21 when at + 2 <= bytes.Length:
                    at = SubBlocks(bytes, at + 2);
                    break;
                case 0x2C when at + 10 <= bytes.Length:
                    at = SubBlocks(bytes, at + 10 + ColourTable(bytes[at + 9]) + 1);
                    images++;
                    break;
                default:
                    return null;
            }

            if (at < 0)
            {
                return null;
            }
        }

        return null;

        static int ColourTable(byte flags) => (flags & 0x80) != 0 ? 3 << ((flags & 0x07) + 1) : 0;
    }

    /// <summary>Where the sub-blocks starting at <paramref name="at"/> end, past the empty one; -1 when they run past the bytes.</summary>
    private static int SubBlocks(ReadOnlySpan<byte> bytes, int at)
    {
        while (at < bytes.Length)
        {
            var length = bytes[at];
            at += 1 + length;
            if (length == 0)
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>
    /// The start-of-image marker, then segments, each with its length: the frame header (a start-of-frame marker, C0 to
    /// CF but for the three that aren't frames), which holds the height and the width, then the start of the scan, whose
    /// data runs to the end-of-image marker.
    /// </summary>
    private static (string ContentType, int Width, int Height)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
        {
            return null;
        }

        (int Width, int Height)? frame = null;
        for (var at = 2; at + 4 <= bytes.Length;)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                // Padding before a marker.
                at++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            if (length < 2 || at + 2 + length > bytes.Length)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (frame is not null || length < 8)
                {
                    return null;
                }

                frame = Sized(BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]));
            }
            else if (marker == 0xDA)
            {
                // The scan: its data, then the end of the image somewhere after it.
                ReadOnlySpan<byte> end = [0xFF, 0xD9];
                return frame is { } size && bytes[(at + 2 + length)..].IndexOf(end) >= 0 ? (JPEG, size.Width, size.Height) : null;
            }
            else if (marker == 0xD9)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }

    /// <summary>
    /// A RIFF file of form WEBP whose length holds, its chunks inside it: the first says which of the three kinds it is,
    /// each keeping the size its own way, and an extended one has an image chunk after it.
    /// </summary>
    private static (string ContentType, int Width, int Height)? WebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var riff = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (riff < 22 || riff > bytes.Length - 8)
        {
            return null;
        }

        var file = bytes[..(8 + (int)riff)];
        List<string> chunks = [];
        for (var at = 12; at < file.Length;)
        {
            if (at + 8 > file.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 4)..]);
            if (length > file.Length - at - 8)
            {
                return null;
            }

            chunks.Add(System.Text.Encoding.ASCII.GetString(file.Slice(at, 4)));
            at += 8 + (int)length + (int)(length & 1);
        }

        var first = chunks[0];
        if (first == "VP8X")
        {
            // The canvas: width and height, each less one, in three bytes.
            return chunks.Skip(1).Any(chunk => chunk is "VP8 " or "VP8L" or "ANMF")
                ? Sized((uint)(file[24] | (file[25] << 8) | (file[26] << 16)) + 1, (uint)(file[27] | (file[28] << 8) | (file[29] << 16)) + 1) is { } canvas
                    ? (WEBP, canvas.Width, canvas.Height)
                    : null
                : null;
        }

        if (first == "VP8L" && file[20] == 0x2F)
        {
            // Lossless: after its signature byte, 14 bits of width less one, then 14 of height less one.
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(file[21..]);
            return Sized((packed & 0x3FFF) + 1, ((packed >> 14) & 0x3FFF) + 1) is { } size ? (WEBP, size.Width, size.Height) : null;
        }

        if (first == "VP8 " && file[23] == 0x9D && file[24] == 0x01 && file[25] == 0x2A)
        {
            // Lossy: a key frame's start code, then 14 bits of width and 14 of height, each with two bits of scaling above.
            return Sized((uint)(BinaryPrimitives.ReadUInt16LittleEndian(file[26..]) & 0x3FFF), (uint)(BinaryPrimitives.ReadUInt16LittleEndian(file[28..]) & 0x3FFF)) is { } size
                ? (WEBP, size.Width, size.Height)
                : null;
        }

        return null;
    }

    /// <summary>
    /// An icon file: its four-byte signature says little, so its directory must hold up too. Every image it lists lies
    /// inside the file, after the directory, and is a PNG that holds up as one, or a bitmap whose header and pixels do,
    /// of the size the directory gives it (a side of 0 there stands for 256 or more) and no more than an icon may be.
    /// The icon's size is its largest image's.
    /// </summary>
    private static (string ContentType, int Width, int Height)? Ico(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ICO_HEADER_BYTES + ICO_ENTRY_BYTES || !bytes[..4].SequenceEqual<byte>([0x00, 0x00, 0x01, 0x00]))
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        var directoryEnd = ICO_HEADER_BYTES + (count * ICO_ENTRY_BYTES);
        if (count == 0 || directoryEnd > bytes.Length)
        {
            return null;
        }

        (int Width, int Height) largest = default;
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(ICO_HEADER_BYTES + (index * ICO_ENTRY_BYTES), ICO_ENTRY_BYTES);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (length == 0 || offset < directoryEnd || offset > bytes.Length || length > bytes.Length - offset)
            {
                return null;
            }

            var image = bytes.Slice((int)offset, (int)length);
            if ((Png(image) is { } png ? (png.Width, png.Height) : Bitmap(image)) is not { } size || !Fits(size.Width, size.Height) ||
                !Matches(entry[0], size.Width) || !Matches(entry[1], size.Height))
            {
                return null;
            }

            if (size.Width * size.Height > largest.Width * largest.Height)
            {
                largest = size;
            }
        }

        return (ICO, largest.Width, largest.Height);

        // A side the directory gives as 0 is 256 or more.
        static bool Matches(byte given, int side) => given == 0 ? side >= 256 : side == given;
    }

    /// <summary>
    /// An icon file's bitmap: a 40-byte header (its height counts the colour rows and the mask's together, so it's twice
    /// the image's), one plane, a known number of bits a pixel, uncompressed, its palette, and every row of its pixels.
    /// </summary>
    private static (int Width, int Height)? Bitmap(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BITMAP_HEADER_BYTES || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != BITMAP_HEADER_BYTES)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var rows = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        var colours = BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]);
        if (planes != 1 || bits is not (1 or 4 or 8 or 24 or 32) || compression != 0 || width is < 1 or > MAX_DIMENSION || rows is < 2 or > 2 * MAX_DIMENSION ||
            rows % 2 != 0 || colours > 256)
        {
            return null;
        }

        var height = rows / 2;
        var palette = bits <= 8 ? 4L * (colours == 0 ? 1 << bits : colours) : 0;
        var pixels = (((long)width * bits + 31) / 32) * 4 * height;
        return BITMAP_HEADER_BYTES + palette + pixels <= bytes.Length ? (width, height) : null;
    }

    /// <summary>A size as read, kept only if it's one an image can have here: the caller refuses the rest.</summary>
    private static (int Width, int Height)? Sized(uint width, uint height) =>
        width <= MAX_DIMENSION && height <= MAX_DIMENSION ? ((int)width, (int)height) : (0, 0);
}
