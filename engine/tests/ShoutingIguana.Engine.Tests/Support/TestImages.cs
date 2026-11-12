using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Images of each kind a site's icon may be, each with the structure of its kind whole: a PNG's chunks with their
/// checksums to its end, a GIF's blocks to its trailer, a JPEG's segments to its scan and its end, a WebP's chunks inside
/// its length, an icon file's images as its directory says. Their pixel data isn't a picture a browser would draw; what
/// tells an image by its bytes doesn't decode pixels.
/// </summary>
internal static class TestImages
{
    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A PNG: its header, one chunk of image data (as long as it takes to make <paramref name="bytes"/>), and its end.</summary>
    public static byte[] Png(int width, int height, int bytes = 64)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        (header[8], header[9]) = (8, 6);
        const int FIXED = 8 + (12 + 13) + 12 + 12;
        return [.. PNG_SIGNATURE, .. PngChunk("IHDR", header), .. PngChunk("IDAT", new byte[Math.Max(1, bytes - FIXED)]), .. PngChunk("IEND", [])];
    }

    /// <summary>A PNG chunk: its length, its name, its data and the checksum of the name and the data.</summary>
    public static byte[] PngChunk(string name, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32.HashToUInt32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    /// <summary>A GIF: its screen, a graphic control extension, one image with its data in a sub-block, and the trailer.</summary>
    public static byte[] Gif(int width, int height)
    {
        var screen = new byte[13];
        "GIF89a"u8.CopyTo(screen);
        BinaryPrimitives.WriteUInt16LittleEndian(screen.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(screen.AsSpan(8), (ushort)height);
        byte[] image = [0x2C, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00];
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(5), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(7), (ushort)height);
        return [.. screen, 0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, .. image, 0x3B];
    }

    /// <summary>A JPEG: its start, an application segment, the frame header that holds its size, the scan and its end.</summary>
    public static byte[] Jpeg(int width, int height)
    {
        byte[] image =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00, 0x12, 0x34, 0x56,
            0xFF, 0xD9
        ];
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(25), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(27), (ushort)width);
        return image;
    }

    /// <summary>A WebP of the extended kind, whose header holds the canvas, with its image in a lossless chunk after.</summary>
    public static byte[] WebPExtended(int width, int height)
    {
        var canvas = new byte[10];
        Write24(canvas.AsSpan(4), width - 1);
        Write24(canvas.AsSpan(7), height - 1);
        return Riff([.. RiffChunk("VP8X", canvas), .. RiffChunk("VP8L", Lossless(width, height))]);
    }

    public static byte[] WebPLossless(int width, int height) => Riff(RiffChunk("VP8L", Lossless(width, height)));

    public static byte[] WebPLossy(int width, int height)
    {
        var frame = new byte[12];
        (frame[3], frame[4], frame[5]) = (0x9D, 0x01, 0x2A);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), (ushort)height);
        return Riff(RiffChunk("VP8 ", frame));
    }

    /// <summary>An icon file holding an image for each size, each a PNG (or a bitmap), one after another behind the directory.</summary>
    public static byte[] Ico(bool bitmaps = false, params (int Width, int Height)[] sizes)
    {
        var images = sizes.Select(size => bitmaps ? Bitmap(size.Width, size.Height) : Png(size.Width, size.Height)).ToList();
        var directory = new byte[6 + (sizes.Length * 16)];
        directory[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(4), (ushort)sizes.Length);
        var offset = directory.Length;
        for (var index = 0; index < sizes.Length; index++)
        {
            var entry = directory.AsSpan(6 + (index * 16), 16);
            entry[0] = (byte)(sizes[index].Width >= 256 ? 0 : sizes[index].Width);
            entry[1] = (byte)(sizes[index].Height >= 256 ? 0 : sizes[index].Height);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)images[index].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            offset += images[index].Length;
        }

        return [.. directory, .. images.SelectMany(image => image)];
    }

    /// <summary>An icon file's 32-bit bitmap: its header (its height twice the image's, for the mask), and every row of its pixels and its mask.</summary>
    public static byte[] Bitmap(int width, int height)
    {
        var mask = ((width + 31) / 32) * 4 * height;
        var image = new byte[40 + (width * 4 * height) + mask];
        BinaryPrimitives.WriteUInt32LittleEndian(image, 40);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14), 32);
        return image;
    }

    /// <summary>An SVG that would run script were it ever drawn as a document.</summary>
    public static byte[] Svg() => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><script>alert(1)</script></svg>");

    private static byte[] Lossless(int width, int height)
    {
        var data = new byte[10];
        data[0] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), (uint)((width - 1) | ((height - 1) << 14)));
        return data;
    }

    private static byte[] RiffChunk(string name, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(name).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Riff(byte[] chunks)
    {
        var file = new byte[12 + chunks.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(4 + chunks.Length));
        "WEBP"u8.CopyTo(file.AsSpan(8));
        chunks.CopyTo(file, 12);
        return file;
    }

    private static void Write24(Span<byte> at, int value) => (at[0], at[1], at[2]) = ((byte)value, (byte)(value >> 8), (byte)(value >> 16));
}
