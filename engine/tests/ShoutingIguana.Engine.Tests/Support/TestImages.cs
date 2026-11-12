using System.Buffers.Binary;
using System.Text;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// The first bytes of images of each kind a site's icon may be, as a file of that kind begins: enough for anything that
/// tells an image by its signature and reads its size from its header. They aren't pictures a browser would draw.
/// </summary>
internal static class TestImages
{
    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A PNG's signature and header chunk, padded with zeros to <paramref name="bytes"/>.</summary>
    public static byte[] Png(int width, int height, int bytes = 64)
    {
        var image = new byte[Math.Max(bytes, 33)];
        PNG_SIGNATURE.CopyTo(image, 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(8), 13);
        "IHDR"u8.CopyTo(image.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(20), (uint)height);
        image[24] = 8;
        image[25] = 6;
        return image;
    }

    public static byte[] Gif(int width, int height)
    {
        var image = new byte[32];
        "GIF89a"u8.CopyTo(image);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(8), (ushort)height);
        return image;
    }

    /// <summary>A JPEG: its start, an application segment, and the frame header that holds its size.</summary>
    public static byte[] Jpeg(int width, int height)
    {
        byte[] image =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9
        ];
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(25), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(27), (ushort)width);
        return image;
    }

    /// <summary>A WebP of the extended kind, whose header holds the canvas.</summary>
    public static byte[] WebPExtended(int width, int height)
    {
        var image = WebP("VP8X");
        Write24(image.AsSpan(24), width - 1);
        Write24(image.AsSpan(27), height - 1);
        return image;
    }

    public static byte[] WebPLossless(int width, int height)
    {
        var image = WebP("VP8L");
        image[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(21), (uint)((width - 1) | ((height - 1) << 14)));
        return image;
    }

    public static byte[] WebPLossy(int width, int height)
    {
        var image = WebP("VP8 ");
        (image[23], image[24], image[25]) = (0x9D, 0x01, 0x2A);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(28), (ushort)height);
        return image;
    }

    /// <summary>An icon file holding an image for each size, each a PNG (or a bitmap), one after another behind the directory.</summary>
    public static byte[] Ico(bool bitmaps = false, params (int Width, int Height)[] sizes)
    {
        const int IMAGE_BYTES = 48;
        var image = new byte[6 + (sizes.Length * 16) + (sizes.Length * IMAGE_BYTES)];
        image[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(4), (ushort)sizes.Length);
        for (var index = 0; index < sizes.Length; index++)
        {
            var entry = image.AsSpan(6 + (index * 16), 16);
            var offset = 6 + (sizes.Length * 16) + (index * IMAGE_BYTES);
            entry[0] = (byte)(sizes[index].Width == 256 ? 0 : sizes[index].Width);
            entry[1] = (byte)(sizes[index].Height == 256 ? 0 : sizes[index].Height);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], IMAGE_BYTES);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            if (bitmaps)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), 40);
            }
            else
            {
                Png(sizes[index].Width, sizes[index].Height, IMAGE_BYTES).CopyTo(image.AsSpan(offset));
            }
        }

        return image;
    }

    /// <summary>An SVG that would run script were it ever drawn as a document.</summary>
    public static byte[] Svg() => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><script>alert(1)</script></svg>");

    private static byte[] WebP(string chunk)
    {
        var image = new byte[40];
        "RIFF"u8.CopyTo(image);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), 32);
        "WEBP"u8.CopyTo(image.AsSpan(8));
        Encoding.ASCII.GetBytes(chunk).CopyTo(image.AsSpan(12));
        return image;
    }

    private static void Write24(Span<byte> at, int value) => (at[0], at[1], at[2]) = ((byte)value, (byte)(value >> 8), (byte)(value >> 16));
}
