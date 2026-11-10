using System.Numerics;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The ids of the links a filter set matches, in crawl order, read once in one pass over the links and kept, so a page
/// anywhere in a filtered links grid costs a page: how many there are, the ids at any place among them, and how many come
/// before a given one (where a cursor left off). A bit per link, with a count kept every few thousand bits, which is 630 KB
/// for the 100k benchmark's 5 million links; a few ids, or ids far apart (a crafted file's), are kept as a sorted list
/// instead, so no file can make one large.
/// </summary>
internal sealed class LinkIdSet
{
    /// <summary>The largest id kept as a bit: 2^30 bits are 128 MB, a crawl of some 20 million pages.</summary>
    private const long MAX_BIT_ID = 1L << 30;

    /// <summary>Bits between kept counts: 64 words.</summary>
    private const int BLOCK_WORDS = 64;

    private readonly ulong[]? _bits;
    private long[] _before = [];
    private List<long>? _adding;
    private long[] _ids = [];

    /// <param name="largestId">The largest link id in the crawl.</param>
    /// <param name="links">How many links it has: ids far sparser than that aren't kept as bits.</param>
    public LinkIdSet(long largestId, long links)
    {
        if (largestId is >= 0 and <= MAX_BIT_ID && largestId <= 4 * links + 4_096)
        {
            _bits = new ulong[(largestId >> 6) + 1];
        }
        else
        {
            _adding = [];
        }
    }

    /// <summary>A set of these ids, in any order.</summary>
    public LinkIdSet(List<long> ids)
    {
        _adding = ids;
        Seal();
    }

    /// <summary>How many ids it holds; set once it's sealed.</summary>
    public long Count { get; private set; }

    public void Add(long id)
    {
        if (_bits is null)
        {
            _adding!.Add(id);
        }
        else if (id >= 0 && (id >> 6) < _bits.Length)
        {
            _bits[id >> 6] |= 1UL << (int)(id & 63);
        }
    }

    /// <summary>Counts the ids, and readies it for <see cref="Range"/> and <see cref="Rank"/>. Nothing is added after.</summary>
    public LinkIdSet Seal()
    {
        if (_bits is null)
        {
            _ids = [.. _adding!.Distinct().Order()];
            _adding = null;
            Count = _ids.Length;
            return this;
        }

        _before = new long[(_bits.Length + BLOCK_WORDS - 1) / BLOCK_WORDS + 1];
        long count = 0;
        for (var word = 0; word < _bits.Length; word++)
        {
            if (word % BLOCK_WORDS == 0)
            {
                _before[word / BLOCK_WORDS] = count;
            }

            count += BitOperations.PopCount(_bits[word]);
        }

        _before[^1] = count;
        Count = count;
        return this;
    }

    /// <summary>The ids at places <paramref name="offset"/> to <paramref name="offset"/> + <paramref name="take"/> in ascending order, as many as there are.</summary>
    public long[] Range(long offset, int take)
    {
        if (offset < 0 || offset >= Count || take <= 0)
        {
            return [];
        }

        var length = (int)Math.Min(take, Count - offset);
        if (_bits is null)
        {
            return _ids[(int)offset..((int)offset + length)];
        }

        // The block holding the first place: the last whose count before it is at most the offset.
        var (block, lastBlock) = (0, _before.Length - 2);
        while (block < lastBlock)
        {
            var middle = (block + lastBlock + 1) / 2;
            if (_before[middle] <= offset)
            {
                block = middle;
            }
            else
            {
                lastBlock = middle - 1;
            }
        }

        var ids = new long[length];
        var found = 0;
        var skip = offset - _before[block];
        for (var word = block * BLOCK_WORDS; word < _bits.Length && found < length; word++)
        {
            var bits = _bits[word];
            var here = BitOperations.PopCount(bits);
            if (skip >= here)
            {
                skip -= here;
                continue;
            }

            while (bits != 0 && found < length)
            {
                if (skip > 0)
                {
                    skip--;
                }
                else
                {
                    ids[found++] = ((long)word << 6) + BitOperations.TrailingZeroCount(bits);
                }

                bits &= bits - 1;
            }
        }

        return ids;
    }

    /// <summary>How many of its ids are at most <paramref name="id"/>.</summary>
    public long Rank(long id)
    {
        if (id < 0)
        {
            return 0;
        }

        if (_bits is null)
        {
            var place = Array.BinarySearch(_ids, id);
            return place >= 0 ? place + 1 : ~place;
        }

        var last = id >> 6;
        if (last >= _bits.Length)
        {
            return Count;
        }

        var count = _before[last / BLOCK_WORDS];
        for (var word = last / BLOCK_WORDS * BLOCK_WORDS; word < last; word++)
        {
            count += BitOperations.PopCount(_bits[word]);
        }

        var upTo = (int)(id & 63);
        var mask = upTo == 63 ? ulong.MaxValue : (1UL << (upTo + 1)) - 1;
        return count + BitOperations.PopCount(_bits[last] & mask);
    }
}
