using System.IO.Compression;
using System.Text;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A page's stored HTML, read a piece at a time, so the source view never holds a large page whole: unpacked and decoded
/// as it's read, kept up to <see cref="UrlInspectorReader.MAX_SOURCE_BYTES"/> (cut on a character's boundary), and the
/// rest only measured, up to <see cref="UrlInspectorReader.MAX_MEASURED_BYTES"/>. It holds the page packed, as stored,
/// and a buffer of 64 KB; the file was read and let go before it was given out. Or why there's no HTML (<see cref="Missing"/>).
/// </summary>
public sealed class HtmlSourceReader : IDisposable
{
    private const int CHUNK_BYTES = 64 * 1024;

    private readonly BrotliStream? _html;
    private readonly CancellationToken _cancellationToken;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly byte[] _chunk = [];
    private int _pendingStart;
    private int _pendingCount;
    private long _kept;
    private long _bytes;
    private bool _ended;
    private bool _flushed;

    internal HtmlSourceReader(HtmlSourceMissing missing) => Missing = missing;

    internal HtmlSourceReader(byte[] packed, CancellationToken cancellationToken)
    {
        _html = new BrotliStream(new MemoryStream(packed, writable: false), CompressionMode.Decompress);
        _chunk = new byte[CHUNK_BYTES];
        _cancellationToken = cancellationToken;
    }

    /// <summary>Why the URL has no HTML to show: null when it has.</summary>
    public HtmlSourceMissing? Missing { get; }

    /// <summary>The HTML was cut at <see cref="UrlInspectorReader.MAX_SOURCE_BYTES"/>; known once <see cref="Read"/> has given 0.</summary>
    public bool Truncated { get; private set; }

    /// <summary>
    /// The HTML's size once unpacked, before any cut, once <see cref="Read"/> has given 0; null before then, and for a page
    /// over <see cref="UrlInspectorReader.MAX_MEASURED_BYTES"/>, which isn't unpacked that far.
    /// </summary>
    public long? Bytes => _ended && _flushed && _bytes <= UrlInspectorReader.MAX_MEASURED_BYTES ? _bytes : null;

    /// <summary>The next piece of the HTML into <paramref name="buffer"/> (two characters or more): how many characters, 0 at its end.</summary>
    /// <exception cref="InvalidDataException">The stored page is damaged.</exception>
    /// <exception cref="OperationCanceledException">The token it was opened with was cancelled.</exception>
    public int Read(Span<char> buffer)
    {
        if (_html is null)
        {
            return 0;
        }

        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_pendingCount > 0)
            {
                _decoder.Convert(_chunk.AsSpan(_pendingStart, _pendingCount), buffer, flush: false, out var used, out var chars, out _);
                if (used == 0 && chars == 0)
                {
                    throw new ArgumentException("Read into a buffer of two characters or more.", nameof(buffer));
                }

                _pendingStart += used;
                _pendingCount -= used;
                if (chars > 0)
                {
                    return chars;
                }

                // The bytes left begin a character the next piece ends: the decoder keeps them.
                continue;
            }

            if (!_ended)
            {
                if (Truncated)
                {
                    Measure();
                    _ended = true;
                    continue;
                }

                var read = _html.Read(_chunk);
                if (read == 0)
                {
                    _ended = true;
                    continue;
                }

                _bytes += read;
                var keep = (int)Math.Clamp(UrlInspectorReader.MAX_SOURCE_BYTES - _kept, 0, read);
                _kept += keep;
                Truncated = keep < read;
                (_pendingStart, _pendingCount) = (0, keep);
                continue;
            }

            if (_flushed)
            {
                return 0;
            }

            // At the end of a page that wasn't cut, a last character left unfinished reads as a replacement character, as
            // decoding it whole would; at a cut, the character the cut went through is left out.
            _flushed = true;
            if (!Truncated)
            {
                _decoder.Convert(ReadOnlySpan<byte>.Empty, buffer, flush: true, out _, out var last, out _);
                if (last > 0)
                {
                    return last;
                }
            }
        }
    }

    public void Dispose() => _html?.Dispose();

    /// <summary>The rest of a page that was cut, unpacked only to measure it, and only so far, so a crafted file can't make it unpack forever.</summary>
    private void Measure()
    {
        int read;
        while (_bytes <= UrlInspectorReader.MAX_MEASURED_BYTES && (read = _html!.Read(_chunk)) > 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _bytes += read;
        }
    }
}
