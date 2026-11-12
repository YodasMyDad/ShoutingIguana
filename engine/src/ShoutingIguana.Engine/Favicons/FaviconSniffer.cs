using System.Buffers.Binary;
using ShoutingIguana.Engine.Favicons.Models;

namespace ShoutingIguana.Engine.Favicons;

/// <summary>
/// Says what a site's icon is by its bytes, never by what the site calls it: a site can serve a page, a script or an
/// SVG (which can carry script) under any name and any Content-Type. Only raster images a browser draws without running
/// anything are images here: PNG, ICO, GIF, JPEG and WebP, each told by its signature and with its size read from its
/// header. Anything else is not an image, and neither is one whose size can't be read or is over
/// <see cref="MAX_DIMENSION"/> pixels a side: a few hundred kilobytes can declare a picture that takes gigabytes to draw.
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

    /// <summary>The image the bytes are, or null when they aren't one the engine keeps.</summary>
    public static FaviconImage? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MAX_BYTES)
        {
            return null;
        }

        var size = Png(bytes) ?? Gif(bytes) ?? Jpeg(bytes) ?? WebP(bytes) ?? Ico(bytes);
        return size is { Width: >= 1 and <= MAX_DIMENSION, Height: >= 1 and <= MAX_DIMENSION } image
            ? new FaviconImage(image.ContentType, image.Width, image.Height)
            : null;
    }

    /// <summary>The 8-byte signature, then the IHDR chunk, which must come first: its length, its name, the width and the height.</summary>
    private static (string ContentType, int Width, int Height)? Png(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && bytes[12..16].SequenceEqual("IHDR"u8)
            ? Sized(PNG, BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]))
            : null;

    private static (string ContentType, int Width, int Height)? Gif(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 10 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8))
            ? Sized(GIF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]))
            : null;

    /// <summary>
    /// The start-of-image marker, then segments, each with its length, up to the frame header (a start-of-frame marker,
    /// C0 to CF but for the three that aren't frames), which holds the height and the width.
    /// </summary>
    private static (string ContentType, int Width, int Height)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
        {
            return null;
        }

        for (var at = 2; at + 9 <= bytes.Length;)
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

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return Sized(JPEG, BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]));
            }

            // The image's data begins, or ends, before any frame header: not a picture a browser could draw.
            if (marker is 0xDA or 0xD9)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            if (length < 2)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }

    /// <summary>A RIFF file of form WEBP, whose first chunk says which of the three kinds it is, each keeping the size its own way.</summary>
    private static (string ContentType, int Width, int Height)? WebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
        {
            // The canvas: width and height, each less one, in three bytes.
            return Sized(WEBP, (uint)(bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1, (uint)(bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1);
        }

        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
        {
            // Lossless: after its signature byte, 14 bits of width less one, then 14 of height less one.
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return Sized(WEBP, (packed & 0x3FFF) + 1, ((packed >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8 "u8) && bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A)
        {
            // Lossy: a key frame's start code, then 14 bits of width and 14 of height, each with two bits of scaling above.
            return Sized(WEBP, (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF), (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF));
        }

        return null;
    }

    /// <summary>
    /// An icon file: its four-byte signature says little, so its directory must hold up too. Every image it lists lies
    /// inside the file, after the directory, and the largest, whose size is the icon's, begins as a PNG or a bitmap does.
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

        (int Width, int Height, int Offset) largest = default;
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(ICO_HEADER_BYTES + (index * ICO_ENTRY_BYTES), ICO_ENTRY_BYTES);

            // A side of 0 stands for 256.
            var (width, height) = (entry[0] == 0 ? 256 : entry[0], entry[1] == 0 ? 256 : entry[1]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (length == 0 || offset < directoryEnd || offset > bytes.Length || length > bytes.Length - offset)
            {
                return null;
            }

            if (width * height > largest.Width * largest.Height)
            {
                largest = (width, height, (int)offset);
            }
        }

        var image = bytes[largest.Offset..];
        var isPng = image.Length >= 8 && image[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var isBitmap = image.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(image) == 40;
        return isPng || isBitmap ? (ICO, largest.Width, largest.Height) : null;
    }

    /// <summary>A size as read, kept only if it's one an image can have here: the caller refuses the rest.</summary>
    private static (string ContentType, int Width, int Height)? Sized(string contentType, uint width, uint height) =>
        width <= int.MaxValue && height <= int.MaxValue ? (contentType, (int)width, (int)height) : (contentType, 0, 0);
}
