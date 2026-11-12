using System.Buffers;
using System.Text;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A crawl's URLs and titles as text in memory, which a search on every keystroke reads instead of the file
/// (<see cref="UrlSearchReader"/>): a search is then a pass over some 70 bytes a URL with no SQLite in it, a few
/// milliseconds on 100,000 URLs whatever is typed, where scanning the file's two tables takes 15 to 40 ms before anything
/// is ranked.
/// <list type="bullet">
/// <item>The text is kept with its ASCII letters lowercased, as a search ignores their case (what SQLite's LIKE does, so
/// the URL grid's search matches the same URLs), a zero byte after each URL and each title, so no match runs from one
/// into the next. Which letters were capitals is kept a bit a byte, so a result is shown as the crawl stored it.</item>
/// <item>Each URL also keeps where its host name and its path start, and whether it's the site's own; each title, the
/// URL it belongs to.</item>
/// <item>It's filled once, by one read of the file, and never changed after <see cref="Seal"/>: any number of searches
/// read it at once.</item>
/// </list>
/// It takes about 106 bytes a URL: 11.4 MB for the 100k benchmark crawl's 112,590.
/// </summary>
internal sealed class SearchText
{
    private const byte SEPARATOR = 0;

    private static readonly byte[] WWW = "www."u8.ToArray();

    private static readonly byte[] SCHEME_END = "://"u8.ToArray();

    private byte[] _urls = new byte[1 << 16];
    private ulong[] _urlCapitals = new ulong[1 << 10];
    private int[] _urlStarts = new int[1 << 10];
    private int[] _ids = new int[1 << 10];
    private byte[] _hostStarts = new byte[1 << 10];
    private ushort[] _pathStarts = new ushort[1 << 10];
    private bool[] _internal = new bool[1 << 10];
    private int[] _titleOfUrl = new int[1 << 10];
    private int _urlBytes;

    private byte[] _titles = new byte[1 << 16];
    private ulong[] _titleCapitals = new ulong[1 << 10];
    private int[] _titleStarts = new int[1 << 10];
    private int[] _urlOfTitle = new int[1 << 10];
    private int _titleBytes;
    private int _titleCount;

    private bool _ascending = true;
    private int _cursor;
    private Dictionary<int, int>? _ordinals;

    /// <summary>How many URLs it holds.</summary>
    public int Count { get; private set; }

    /// <summary>The memory it takes, in bytes.</summary>
    public long Bytes =>
        _urls.Length + _titles.Length + ((long)_urlCapitals.Length + _titleCapitals.Length) * sizeof(ulong) +
        ((long)_urlStarts.Length + _ids.Length + _titleOfUrl.Length + _titleStarts.Length + _urlOfTitle.Length) * sizeof(int) +
        _hostStarts.Length + (long)_pathStarts.Length * sizeof(ushort) + _internal.Length;

    /// <summary>One more URL, its text UTF-8 as the file holds it. An id no URL of a crawl has is left out.</summary>
    public void AddUrl(long id, ReadOnlySpan<byte> url, bool isInternal)
    {
        if (id is < 1 or > int.MaxValue)
        {
            return;
        }

        url = UpToZero(url);
        var ordinal = Count;
        if (ordinal + 1 >= _urlStarts.Length)
        {
            var size = _urlStarts.Length * 2;
            Array.Resize(ref _urlStarts, size);
            Array.Resize(ref _ids, size);
            Array.Resize(ref _hostStarts, size);
            Array.Resize(ref _pathStarts, size);
            Array.Resize(ref _internal, size);
            Array.Resize(ref _titleOfUrl, size);
        }

        Append(ref _urls, ref _urlCapitals, ref _urlBytes, url);
        _ascending &= ordinal == 0 || id > _ids[ordinal - 1];
        _ids[ordinal] = (int)id;
        _urlStarts[ordinal] = _urlBytes - url.Length - 1;
        _urlStarts[ordinal + 1] = _urlBytes;
        var scheme = url.IndexOf(SCHEME_END);
        var host = scheme < 0 ? 0 : scheme + SCHEME_END.Length;
        var path = url[host..].IndexOf((byte)'/');
        _hostStarts[ordinal] = (byte)Math.Min(host, byte.MaxValue);
        _pathStarts[ordinal] = (ushort)Math.Min(path < 0 ? url.Length : host + path, ushort.MaxValue);
        _internal[ordinal] = isInternal;
        _titleOfUrl[ordinal] = -1;
        Count++;
    }

    /// <summary>The title of a URL added before it. One for a URL it doesn't have, or a second for one it has, is left out.</summary>
    public void AddTitle(long urlId, ReadOnlySpan<byte> title)
    {
        if (urlId is < 1 or > int.MaxValue || OrdinalOf((int)urlId) is not { } ordinal || _titleOfUrl[ordinal] >= 0)
        {
            return;
        }

        title = UpToZero(title);
        if (_titleCount + 1 >= _titleStarts.Length)
        {
            var size = _titleStarts.Length * 2;
            Array.Resize(ref _titleStarts, size);
            Array.Resize(ref _urlOfTitle, size);
        }

        Append(ref _titles, ref _titleCapitals, ref _titleBytes, title);
        _titleStarts[_titleCount] = _titleBytes - title.Length - 1;
        _titleStarts[_titleCount + 1] = _titleBytes;
        _urlOfTitle[_titleCount] = ordinal;
        _titleOfUrl[ordinal] = _titleCount;
        _titleCount++;
    }

    /// <summary>Nothing is added after: what it holds is cut to size.</summary>
    public SearchText Seal()
    {
        _ordinals = null;
        Array.Resize(ref _urls, _urlBytes);
        Array.Resize(ref _urlCapitals, (_urlBytes + 63) >> 6);
        Array.Resize(ref _urlStarts, Count + 1);
        Array.Resize(ref _ids, Count);
        Array.Resize(ref _hostStarts, Count);
        Array.Resize(ref _pathStarts, Count);
        Array.Resize(ref _internal, Count);
        Array.Resize(ref _titleOfUrl, Count);
        Array.Resize(ref _titles, _titleBytes);
        Array.Resize(ref _titleCapitals, (_titleBytes + 63) >> 6);
        Array.Resize(ref _titleStarts, _titleCount + 1);
        Array.Resize(ref _urlOfTitle, _titleCount);
        return this;
    }

    public int Id(int ordinal) => _ids[ordinal];

    public bool IsInternal(int ordinal) => _internal[ordinal];

    /// <summary>The URL as the crawl stored it.</summary>
    public string Url(int ordinal) => Stored(_urls, _urlCapitals, _urlStarts[ordinal], _urlStarts[ordinal + 1] - 1);

    /// <summary>The URL's title as the crawl stored it, or null when it has none.</summary>
    public string? Title(int ordinal) =>
        _titleOfUrl[ordinal] is var title and >= 0 ? Stored(_titles, _titleCapitals, _titleStarts[title], _titleStarts[title + 1] - 1) : null;

    /// <summary>A search's text as it's looked for: UTF-8, its ASCII letters lowercased.</summary>
    public static byte[] Needle(string text)
    {
        var needle = Encoding.UTF8.GetBytes(text);
        for (var index = 0; index < needle.Length; index++)
        {
            if (needle[index] is >= (byte)'A' and <= (byte)'Z')
            {
                needle[index] += 0x20;
            }
        }

        return needle;
    }

    /// <summary>
    /// The URLs whose URL or title contains the text, counted, and the best few of them, best first: by rank
    /// (<see cref="UrlSearchRank"/>), then the site's own URLs before others, a match in the URL before one in the title,
    /// the shorter URL (or title) first, and the URL the crawl found first.
    /// </summary>
    /// <param name="needle">The text, from <see cref="Needle"/>: not empty, and without a zero byte.</param>
    public SearchHits Search(ReadOnlySpan<byte> needle, int limit, CancellationToken cancellationToken = default)
    {
        var words = (Count + 63) >> 6;
        var matched = ArrayPool<ulong>.Shared.Rent(Math.Max(words, 1));
        try
        {
            matched.AsSpan(0, words).Clear();
            Best urls = new(limit), titles = new(limit);
            var matching = SearchUrls(needle, matched, ref urls, cancellationToken) + SearchTitles(needle, matched, ref titles, cancellationToken);

            // A URL is as good as the better of its two matches. The best few are among the best few by each: a URL left
            // out of both lists has that many better than it in one of them.
            List<(ulong Score, int Ordinal)> merged = [.. urls.Entries(), .. titles.Entries()];
            merged.Sort((left, right) => left.Score.CompareTo(right.Score));
            HashSet<int> taken = [];
            List<SearchHit> best = [];
            foreach (var (score, ordinal) in merged)
            {
                if (best.Count < limit && taken.Add(ordinal))
                {
                    best.Add(new SearchHit(ordinal, (UrlSearchRank)(score >> RANK_SHIFT), (UrlSearchField)((score >> FIELD_SHIFT) & 1)));
                }
            }

            return new SearchHits(matching, best);
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(matched);
        }
    }

    private const int RANK_SHIFT = 50;
    private const int EXTERNAL_SHIFT = 49;
    private const int FIELD_SHIFT = 48;
    private const int LENGTH_SHIFT = 32;

    /// <summary>How often a pass over the text looks at its token: every so many URLs matched.</summary>
    private const int CANCEL_EVERY = 1 << 14;

    /// <summary>A match's place in the order, smaller first: its rank, whose URL it is, where it is, how long that is, and the URL's id.</summary>
    private ulong Score(UrlSearchRank rank, UrlSearchField field, int ordinal, int length) =>
        ((ulong)rank << RANK_SHIFT) | (_internal[ordinal] ? 0 : 1UL << EXTERNAL_SHIFT) | ((ulong)field << FIELD_SHIFT) |
        ((ulong)Math.Min(length, ushort.MaxValue) << LENGTH_SHIFT) | (uint)_ids[ordinal];

    private long SearchUrls(ReadOnlySpan<byte> needle, ulong[] matched, ref Best best, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> text = _urls;
        var bare = needle.TrimEnd((byte)'/');
        var slash = needle[0] == (byte)'/';
        ReadOnlySpan<byte> queryOrFragment = [(byte)'?', (byte)'#'];
        long matching = 0;
        var ordinal = 0;
        var from = 0;
        while (from < text.Length)
        {
            var found = text[from..].IndexOf(needle);
            if (found < 0)
            {
                break;
            }

            found += from;
            while (_urlStarts[ordinal + 1] <= found)
            {
                ordinal++;
            }

            var start = _urlStarts[ordinal];
            var url = text[start..(_urlStarts[ordinal + 1] - 1)];
            int host = _hostStarts[ordinal], path = _pathStarts[ordinal];
            matched[ordinal >> 6] |= 1UL << (ordinal & 63);
            if ((++matching & (CANCEL_EVERY - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var rank = UrlSearchRank.Anywhere;
            var whole = url.TrimEnd((byte)'/');
            var tail = whole.Length - bare.Length;
            if (tail >= 0 && (tail == 0 || tail == host || tail == path || tail == path + 1) && whole[tail..].SequenceEqual(bare))
            {
                rank = UrlSearchRank.Exact;
            }
            else
            {
                // Each place the text is, until one starts the host name (with its www. or after it) or a segment of the
                // path: never in the scheme, and never in the query string or the fragment, whatever slashes they hold.
                // Text that starts with a slash starts a segment wherever that slash is one of the path's.
                var query = url[path..].IndexOfAny(queryOrFragment) is var mark and >= 0 ? path + mark : url.Length;
                for (var at = found - start; at >= 0;)
                {
                    if (at == host || (at == host + WWW.Length && url[host..].StartsWith(WWW)) ||
                        (at >= path && at < query && (slash || (at > 0 && url[at - 1] == (byte)'/'))))
                    {
                        rank = UrlSearchRank.Start;
                        break;
                    }

                    var next = url[(at + 1)..].IndexOf(needle);
                    at = next < 0 ? -1 : at + 1 + next;
                }
            }

            best.Offer(Score(rank, UrlSearchField.Url, ordinal, url.Length), ordinal);
            from = _urlStarts[++ordinal];
        }

        return matching;
    }

    /// <summary>The titles that contain the text; counts only the URLs whose own text didn't.</summary>
    private long SearchTitles(ReadOnlySpan<byte> needle, ulong[] matched, ref Best best, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> text = _titles;
        long matching = 0;
        var found = 0L;
        var title = 0;
        var from = 0;
        while (from < text.Length)
        {
            var next = text[from..].IndexOf(needle);
            if (next < 0)
            {
                break;
            }

            var hit = from + next;
            while (_titleStarts[title + 1] <= hit)
            {
                title++;
            }

            var start = _titleStarts[title];
            var words = text[start..(_titleStarts[title + 1] - 1)];
            var ordinal = _urlOfTitle[title];
            if ((matched[ordinal >> 6] & (1UL << (ordinal & 63))) == 0)
            {
                matched[ordinal >> 6] |= 1UL << (ordinal & 63);
                matching++;
            }

            if ((++found & (CANCEL_EVERY - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Each place the text is, until one starts the title or follows a character that isn't a letter or a digit.
            var rank = UrlSearchRank.Anywhere;
            for (var at = hit - start; at >= 0;)
            {
                if (at == 0 || !EndsInWord(words[..at]))
                {
                    rank = UrlSearchRank.Start;
                    break;
                }

                var later = words[(at + 1)..].IndexOf(needle);
                at = later < 0 ? -1 : at + 1 + later;
            }

            best.Offer(Score(rank, UrlSearchField.Title, ordinal, words.Length), ordinal);
            from = _titleStarts[++title];
        }

        return matching;
    }

    /// <summary>Whether the text ends in a letter or a digit, so what follows it carries a word on.</summary>
    private static bool EndsInWord(ReadOnlySpan<byte> text) =>
        text[^1] < 0x80
            ? char.IsAsciiLetterOrDigit((char)text[^1])
            : Rune.DecodeLastFromUtf8(text, out var last, out _) == OperationStatus.Done && Rune.IsLetterOrDigit(last);

    /// <summary>Where a URL with this id is, or null when there's none. Titles come in the URLs' order, so the next is mostly the next.</summary>
    private int? OrdinalOf(int id)
    {
        if (!_ascending)
        {
            _ordinals ??= Enumerable.Range(0, Count).GroupBy(ordinal => _ids[ordinal]).ToDictionary(same => same.Key, same => same.First());
            return _ordinals.TryGetValue(id, out var known) ? known : null;
        }

        if (_cursor < Count && _ids[_cursor] <= id)
        {
            while (_cursor < Count && _ids[_cursor] < id)
            {
                _cursor++;
            }

            return _cursor < Count && _ids[_cursor] == id ? _cursor : null;
        }

        var index = Array.BinarySearch(_ids, 0, Count, id);
        return index >= 0 ? index : null;
    }

    /// <summary>Text as a search sees it: SQLite's LIKE takes a zero byte for a text's end.</summary>
    private static ReadOnlySpan<byte> UpToZero(ReadOnlySpan<byte> text) => text.IndexOf(SEPARATOR) is var zero and >= 0 ? text[..zero] : text;

    /// <summary>Adds a text and the zero byte after it, its ASCII capitals lowercased and their places marked.</summary>
    private static void Append(ref byte[] buffer, ref ulong[] capitals, ref int length, ReadOnlySpan<byte> text)
    {
        var needed = (long)length + text.Length + 1;
        if (needed > Array.MaxLength)
        {
            throw new InvalidOperationException("This crawl has more text in its URLs and titles than a search can hold.");
        }

        if (needed > buffer.Length)
        {
            Array.Resize(ref buffer, (int)Math.Min(Array.MaxLength, Math.Max(needed, buffer.Length * 2L)));
        }

        if (((needed + 63) >> 6) > capitals.Length)
        {
            Array.Resize(ref capitals, (int)Math.Max((needed + 63) >> 6, capitals.Length * 2L));
        }

        var target = buffer.AsSpan(length, text.Length);
        var done = 0;
        while (done < text.Length)
        {
            // Stops at the first byte that isn't ASCII, which is copied as it is, with those like it that follow.
            Ascii.ToLower(text[done..], target[done..], out var written);
            done += written;
            while (done < text.Length && text[done] >= 0x80)
            {
                target[done] = text[done];
                done++;
            }
        }

        if (!text.SequenceEqual(target))
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != target[index])
                {
                    capitals[(length + index) >> 6] |= 1UL << ((length + index) & 63);
                }
            }
        }

        buffer[length + text.Length] = SEPARATOR;
        length += text.Length + 1;
    }

    /// <summary>The text from <paramref name="start"/> up to <paramref name="end"/> as it was stored, its capitals put back.</summary>
    private static string Stored(byte[] buffer, ulong[] capitals, int start, int end)
    {
        var size = end - start;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
        try
        {
            buffer.AsSpan(start, size).CopyTo(rented);
            for (var index = 0; index < size; index++)
            {
                if ((capitals[(start + index) >> 6] & (1UL << ((start + index) & 63))) != 0)
                {
                    rented[index] -= 0x20;
                }
            }

            return Encoding.UTF8.GetString(rented, 0, size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>The best few matches so far, smallest score first.</summary>
    private struct Best(int capacity)
    {
        private readonly ulong[] _scores = new ulong[capacity];
        private readonly int[] _ordinals = new int[capacity];
        private int _count;

        public void Offer(ulong score, int ordinal)
        {
            if (_count == capacity && score >= _scores[_count - 1])
            {
                return;
            }

            var at = _count < capacity ? _count++ : _count - 1;
            for (; at > 0 && _scores[at - 1] > score; at--)
            {
                _scores[at] = _scores[at - 1];
                _ordinals[at] = _ordinals[at - 1];
            }

            _scores[at] = score;
            _ordinals[at] = ordinal;
        }

        public readonly IEnumerable<(ulong Score, int Ordinal)> Entries()
        {
            for (var index = 0; index < _count; index++)
            {
                yield return (_scores[index], _ordinals[index]);
            }
        }
    }
}
