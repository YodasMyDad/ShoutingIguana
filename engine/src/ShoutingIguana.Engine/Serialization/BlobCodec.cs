using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Serialization;

/// <summary>Brotli-compressed blobs for detail JSON and stored HTML (typically 5-10x smaller than raw).</summary>
public static class BlobCodec
{
    private const int QUALITY = 4;
    private const int WINDOW_BITS = 22;

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BrotliEncoder.GetMaxCompressedLength(data.Length));
        try
        {
            if (!BrotliEncoder.TryCompress(data, buffer, out var written, QUALITY, WINDOW_BITS))
            {
                throw new InvalidOperationException("Brotli compression failed despite a maximum-size buffer.");
            }

            return buffer.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(data.Length * 4);
        brotli.CopyTo(output);
        return output.ToArray();
    }

    public static byte[] CompressText(string text) => Compress(Encoding.UTF8.GetBytes(text));

    public static string DecompressText(byte[] data) => Encoding.UTF8.GetString(Decompress(data));

    public static byte[] EncodeDetails(ResponseDetails details) =>
        Compress(JsonSerializer.SerializeToUtf8Bytes(details, EngineJsonContext.Default.ResponseDetails));

    public static ResponseDetails DecodeDetails(byte[] data) =>
        JsonSerializer.Deserialize(Decompress(data), EngineJsonContext.Default.ResponseDetails)
        ?? throw new InvalidDataException("Response details blob is empty.");
}
