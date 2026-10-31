namespace ShoutingIguana.Engine.Sitemaps;

/// <summary>
/// Reads a stream with a cancellation token on every read, also for readers that read without one, as XmlReader does.
/// A sitemap that stalls part-way then ends at its timeout or at a stop, instead of holding its reader for good. The
/// stream stays the caller's to dispose.
/// </summary>
internal sealed class CancellableReadStream(Stream inner, CancellationToken cancellationToken) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken readToken = default) =>
        !readToken.CanBeCanceled || readToken == cancellationToken
            ? inner.ReadAsync(buffer, cancellationToken)
            : ReadLinkedAsync(buffer, readToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken readToken) =>
        ReadAsync(buffer.AsMemory(offset, count), readToken).AsTask();

    /// <summary>A blocking read can't be cancelled part-way, but it doesn't start once the token is cancelled.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.Read(buffer, offset, count);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private async ValueTask<int> ReadLinkedAsync(Memory<byte> buffer, CancellationToken readToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readToken);
        return await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
    }
}
