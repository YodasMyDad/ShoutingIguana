using System.Buffers;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>A response body held in a rented array. Dispose to hand the array back to the pool.</summary>
internal readonly struct PooledBuffer(byte[] array, int length, bool truncated, long transferBytes) : IDisposable
{
    public ReadOnlySpan<byte> Span => array.AsSpan(0, length);
    public int Length => length;
    public bool Truncated => truncated;
    public long TransferBytes => transferBytes;

    public void Dispose() => ArrayPool<byte>.Shared.Return(array);
}
