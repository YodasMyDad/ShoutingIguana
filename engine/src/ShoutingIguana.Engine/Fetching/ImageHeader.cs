using System.Buffers.Binary;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// An image's own size in pixels, read from the first bytes of its file: PNG (its IHDR chunk), GIF (the logical screen),
/// JPEG (the first start-of-frame segment, found by walking the segments before it by their lengths), WebP (VP8, VP8L or
/// VP8X) and, best effort, AVIF (its <c>ispe</c> property). The format is told by the bytes, not the content type, which
/// servers get wrong. SVG and every other format have none. Pure: the fetcher reads the bytes and asks after each read.
/// </summary>
internal static class ImageHeader
{
    /// <summary>The most of a JPEG read for its size: EXIF data and a colour profile can come before its frame header.</summary>
    public const int MAX_JPEG_BYTES = 256 * 1024;

    /// <summary>The most of any other image read for its size: PNG, GIF and WebP say it in their first 30 bytes, AVIF in its first boxes.</summary>
    public const int MAX_OTHER_BYTES = 64 * 1024;

    private static ReadOnlySpan<byte> PNG => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>What the bytes read so far say.</summary>
    public enum Result
    {
        /// <summary>The size is known.</summary>
        Found,

        /// <summary>More bytes could tell it.</summary>
        NeedMore,

        /// <summary>No more bytes will: not a format with a size in its header, or a damaged one.</summary>
        Unknown
    }

    /// <summary>The most of a file starting with these bytes worth reading for its size.</summary>
    public static int MostToRead(ReadOnlySpan<byte> head) => head.Length >= 2 && head[0] == 0xFF && head[1] == 0xD8 ? MAX_JPEG_BYTES : MAX_OTHER_BYTES;

    /// <summary>The size the first bytes of a file give, if they give one yet.</summary>
    public static Result TryRead(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        // Every format says what it is in its first 12 bytes.
        if (head.Length < 12)
        {
            return Result.NeedMore;
        }

        var result = head switch
        {
            _ when head.StartsWith(PNG) => Png(head, out width, out height),
            _ when head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8) => Gif(head, out width, out height),
            [0xFF, 0xD8, ..] => Jpeg(head, out width, out height),
            _ when head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) => WebP(head, out width, out height),
            _ when head[4..8].SequenceEqual("ftyp"u8) => Avif(head, out width, out height),
            _ => Result.Unknown
        };

        return result == Result.Found && (width <= 0 || height <= 0) ? Result.Unknown : result;
    }

    private static Result Png(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        if (head.Length < 24)
        {
            return Result.NeedMore;
        }

        if (!head[12..16].SequenceEqual("IHDR"u8))
        {
            return Result.Unknown;
        }

        width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head[16..]), int.MaxValue);
        height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head[20..]), int.MaxValue);
        return Result.Found;
    }

    private static Result Gif(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = BinaryPrimitives.ReadUInt16LittleEndian(head[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(head[8..]);
        return Result.Found;
    }

    /// <summary>
    /// Walks the segments from the start of the image to the first start of frame (SOF0 to SOF15, but for DHT, JPG and
    /// DAC, which share the range), skipping each other segment by its length. A scan before any frame header means none.
    /// </summary>
    private static Result Jpeg(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        var at = 2;
        while (true)
        {
            // Markers may be padded with any number of 0xFF.
            while (at < head.Length && head[at] == 0xFF && at + 1 < head.Length && head[at + 1] == 0xFF)
            {
                at++;
            }

            if (at + 4 > head.Length)
            {
                return Result.NeedMore;
            }

            if (head[at] != 0xFF)
            {
                return Result.Unknown;
            }

            var marker = head[at + 1];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return Result.Unknown;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(head[(at + 2)..]);
            if (length < 2)
            {
                return Result.Unknown;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (at + 9 > head.Length)
                {
                    return Result.NeedMore;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(head[(at + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(head[(at + 7)..]);
                return Result.Found;
            }

            at += 2 + length;
        }
    }

    private static Result WebP(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        if (head.Length < 30)
        {
            return Result.NeedMore;
        }

        var chunk = head[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // A key frame's start code, then 14 bits of width and of height.
            if (head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A)
            {
                return Result.Unknown;
            }

            width = BinaryPrimitives.ReadUInt16LittleEndian(head[26..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(head[28..]) & 0x3FFF;
            return Result.Found;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (head[20] != 0x2F)
            {
                return Result.Unknown;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(head[21..]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return Result.Found;
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = (head[24] | (head[25] << 8) | (head[26] << 16)) + 1;
            height = (head[27] | (head[28] << 8) | (head[29] << 16)) + 1;
            return Result.Found;
        }

        return Result.Unknown;
    }

    /// <summary>
    /// An AVIF's (or HEIF's) image spatial extents: the largest <c>ispe</c> property in its property container (<c>ipco</c>),
    /// the primary image being at least as large as its thumbnails. Read once the whole container is: a file whose
    /// properties come after its first 64 KB has none.
    /// </summary>
    private static Result Avif(ReadOnlySpan<byte> head, out int width, out int height)
    {
        width = height = 0;
        var brand = head[8..12];
        if (!brand.SequenceEqual("avif"u8) && !brand.SequenceEqual("avis"u8) && !brand.SequenceEqual("heic"u8) && !brand.SequenceEqual("mif1"u8))
        {
            return Result.Unknown;
        }

        // The container's type follows its 4-byte size.
        var type = head.IndexOf("ipco"u8);
        if (type < 4 || type + 4 > head.Length)
        {
            return Result.NeedMore;
        }

        var start = type - 4;
        var size = BinaryPrimitives.ReadUInt32BigEndian(head[start..]);
        if (size < 8)
        {
            return Result.Unknown;
        }

        if (start + (long)size > head.Length)
        {
            return Result.NeedMore;
        }

        var properties = head[(type + 4)..(start + (int)size)];
        var at = 0;
        while (at < properties.Length && properties[at..].IndexOf("ispe"u8) is var index and >= 0)
        {
            var box = at + index;
            if (box + 16 > properties.Length)
            {
                break;
            }

            // The box's type, then its version and flags, then the width and height.
            var w = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(properties[(box + 8)..]), int.MaxValue);
            var h = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(properties[(box + 12)..]), int.MaxValue);
            if ((long)w * h > (long)width * height)
            {
                (width, height) = (w, h);
            }

            at = box + 4;
        }

        return width > 0 ? Result.Found : Result.Unknown;
    }
}
