using System.Buffers;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Collects a page's main-content text in a pooled buffer, so large pages do not allocate large strings
/// on every parse.
/// </summary>
internal sealed class TextAccumulator : IDisposable
{
    private const int INITIAL_CHARS = 16 * 1024;
    private const int MAX_CHARS = 2_000_000;

    private char[] _buffer = ArrayPool<char>.Shared.Rent(INITIAL_CHARS);
    private int _length;

    public void Append(ReadOnlySpan<char> value)
    {
        var room = MAX_CHARS - _length;
        if (room <= 0 || value.IsEmpty)
        {
            return;
        }

        if (value.Length > room)
        {
            value = value[..room];
        }

        EnsureCapacity(_length + value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>
    /// What separates one block of the text from the next (a paragraph, a list item, a heading, a line break): white space to
    /// everything that reads words, and to <see cref="TextMeasures.SentencesAndSyllables"/> where one block ends.
    /// </summary>
    public const char BLOCK_MARK = '\u2029';

    public void AppendSeparator()
    {
        if (_length > 0 && _buffer[_length - 1] != BLOCK_MARK)
        {
            Append([BLOCK_MARK]);
        }
    }

    public ReadOnlySpan<char> AsSpan() => _buffer.AsSpan(0, _length);

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = [];
        _length = 0;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length)
        {
            return;
        }

        var larger = ArrayPool<char>.Shared.Rent(Math.Max(needed, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(larger);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
