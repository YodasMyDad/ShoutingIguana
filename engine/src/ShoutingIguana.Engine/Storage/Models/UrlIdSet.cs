using System.Globalization;
using System.Numerics;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The URL ids a condition on the crawl's text matched (a title, a search, a check's URLs), read once and kept, so the
/// scan isn't run again for the count and each page: a bit per id, which a row is tested against for a few tens of
/// nanoseconds, with their count, and while there are few, the ids as a JSON array for <c>json_each</c>, which drives a
/// query through them. A crawl of a million URLs takes 140 KB. Ids far past the crawl's (a crafted file's) are kept in a
/// hash set instead, so no file can make one large.
/// </summary>
internal sealed class UrlIdSet
{
    /// <summary>The largest id kept as a bit: past it, a set is a hash set. 2^27 bits are 16 MB.</summary>
    private const long MAX_BIT_ID = 1L << 27;

    private readonly ulong[]? _bits;
    private readonly HashSet<long>? _sparse;

    /// <param name="largestId">The largest URL id in the crawl, which sizes the bits.</param>
    public UrlIdSet(long largestId)
    {
        if (largestId is >= 0 and <= MAX_BIT_ID)
        {
            _bits = new ulong[(largestId >> 6) + 1];
        }
        else
        {
            _sparse = [];
        }
    }

    /// <summary>How many ids it holds; set once it's sealed.</summary>
    public long Count { get; private set; }

    /// <summary>The ids in order as a JSON array, when there are at most the few <see cref="Seal"/> was given; else null.</summary>
    public string? Json { get; private set; }

    public bool Contains(long id) =>
        _bits is not null
            ? id >= 0 && (id >> 6) < _bits.Length && (_bits[id >> 6] & (1UL << (int)(id & 63))) != 0
            : _sparse!.Contains(id);

    public void Add(long id)
    {
        if (_bits is null)
        {
            _sparse!.Add(id);
        }
        else if (id >= 0 && (id >> 6) < _bits.Length)
        {
            _bits[id >> 6] |= 1UL << (int)(id & 63);
        }
    }

    /// <summary>Counts the ids, and lists them when there are at most <paramref name="listed"/>. Nothing is added after.</summary>
    public UrlIdSet Seal(int listed)
    {
        Count = _bits is null ? _sparse!.Count : _bits.Sum(word => (long)BitOperations.PopCount(word));
        if (Count <= listed)
        {
            Json = $"[{string.Join(",", Ids().Select(id => id.ToString(CultureInfo.InvariantCulture)))}]";
        }

        return this;
    }

    /// <summary>The ids in every one of <paramref name="all"/> and in none of <paramref name="none"/>; <paramref name="all"/> isn't empty.</summary>
    public static long CountMatching(IReadOnlyList<UrlIdSet> all, IReadOnlyList<UrlIdSet> none)
    {
        var smallest = all.MinBy(set => set.Count)!;
        if (all.Concat(none).Any(set => set._bits is null || set._bits.Length != smallest._bits?.Length))
        {
            return smallest.Ids().LongCount(id => all.All(set => set.Contains(id)) && !none.Any(set => set.Contains(id)));
        }

        long count = 0;
        for (var word = 0; word < smallest._bits!.Length; word++)
        {
            var bits = ulong.MaxValue;
            foreach (var set in all)
            {
                bits &= set._bits![word];
            }

            foreach (var set in none)
            {
                bits &= ~set._bits![word];
            }

            count += BitOperations.PopCount(bits);
        }

        return count;
    }

    /// <summary>The ids in any of the sets.</summary>
    public static long CountAny(IReadOnlyList<UrlIdSet> sets)
    {
        if (sets.Count == 1)
        {
            return sets[0].Count;
        }

        if (sets.Any(set => set._bits is null || set._bits.Length != sets[0]._bits?.Length))
        {
            return sets.SelectMany(set => set.Ids()).Distinct().LongCount();
        }

        long count = 0;
        for (var word = 0; word < sets[0]._bits!.Length; word++)
        {
            var bits = 0UL;
            foreach (var set in sets)
            {
                bits |= set._bits![word];
            }

            count += BitOperations.PopCount(bits);
        }

        return count;
    }

    private IEnumerable<long> Ids()
    {
        if (_bits is null)
        {
            foreach (var id in _sparse!.Order())
            {
                yield return id;
            }

            yield break;
        }

        for (var word = 0; word < _bits.Length; word++)
        {
            var bits = _bits[word];
            while (bits != 0)
            {
                yield return ((long)word << 6) + BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
            }
        }
    }
}
