using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Titles, descriptions, H1s and main content shared between indexable pages. Each page contributes a 64-bit hash per
/// signal (a 256-bit fingerprint for near duplicates), so memory stays at a few dozen bytes per page, and groups are
/// found by sorting rather than comparing every pair. The pages of a paginated series (joined by rel=next/prev) repeat
/// their H1 by design, so that isn't reported; a title or description they repeat is, as each page should say which one
/// it is.
/// </summary>
internal sealed class DuplicateRules(AnalysisSettings settings) : ISiteRule
{
    /// <summary>
    /// The least a bucket of the near-duplicate search is compared in full up to; past 2 million pages, the limit is four
    /// times the pages unrelated pages put in a bucket on average (pages / 2^16 at the default threshold). Unrelated pages
    /// never fill one that far, at any crawl size, so a fuller bucket holds pages that share much of their text: near
    /// copies of one another, or a heavy template. There, sorted by the lane, each page is compared with the half as many
    /// after it, so no bucket costs more per page than that many comparisons.
    /// </summary>
    private const int MIN_FULL_BUCKET = 128;

    /// <summary>
    /// The counting sort's buckets: a key of more bits (a stricter threshold's, with fewer and wider blocks) is hashed down
    /// to this, and pages it puts together by chance are merely compared.
    /// </summary>
    private const int MAX_KEY_BITS = 20;

    private const ulong GOLDEN_RATIO = 0x9E3779B97F4A7C15;

    private readonly List<(ulong Hash, int Id)> _titles = [];
    private readonly List<(ulong Hash, int Id)> _descriptions = [];
    private readonly List<(ulong Hash, int Id)> _h1s = [];
    private readonly List<(ulong Hash, int Id)> _content = [];
    private readonly List<PageFingerprint> _fingerprints = [];
    private readonly List<(int From, int To)> _pagination = [];

    // The fingerprints of a bucket too full to compare in full, gathered in its order (see CompareBucket).
    private PageFingerprint[] _bucket = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.TITLE_DUPLICATE, IssueCatalog.DESCRIPTION_DUPLICATE, IssueCatalog.H1_DUPLICATE, IssueCatalog.DUPLICATE_CONTENT, IssueCatalog.NEAR_DUPLICATE_CONTENT];

    /// <summary>
    /// Pages the near-duplicate check would compare without the whole of their fingerprint, because an earlier version
    /// crawled them. Any page could be the near copy of one of them, so with any the check can't be made; the analysis
    /// skips it, and <see cref="Finish"/> doesn't search.
    /// </summary>
    public int PagesWithoutFingerprint { get; private set; }

    /// <summary>Pages the near-duplicate check compares: indexable, with enough words, and with the whole of their fingerprint.</summary>
    public int PagesWithFingerprint => _fingerprints.Count;

    /// <summary>Tests only: pairs of pages the near-duplicate search has compared, which its bounds hold to.</summary>
    internal long Comparisons { get; private set; }

    public void Observe(AnalyzedPage page)
    {
        // Every page's rel=next/prev, so a noindex page in the middle still joins the series.
        foreach (var link in page.Links)
        {
            if (link.Kind == LinkKind.Pagination && link.ToId != page.UrlId)
            {
                _pagination.Add((page.UrlId, link.ToId));
            }
        }

        if (!page.IsIndexable)
        {
            return;
        }

        var facts = page.Facts;
        AddText(_titles, facts.Titles.FirstOrDefault(), page.UrlId);
        AddText(_descriptions, facts.MetaDescriptions.FirstOrDefault(), page.UrlId);
        AddText(_h1s, facts.Headings.FirstOrDefault(heading => heading.Level == 1 && heading.Text.Length > 0)?.Text, page.UrlId);
        if (facts.WordCount >= settings.DuplicateMinWordCount)
        {
            _content.Add((facts.ContentHash, page.UrlId));
            if (facts.SimHashLanes is { } lanes)
            {
                _fingerprints.Add(new PageFingerprint(page.UrlId, facts.ContentHash, facts.SimHash, lanes));
            }
            else
            {
                PagesWithoutFingerprint++;
            }
        }
    }

    public IEnumerable<IssueHit> Finish() =>
        Groups(_titles, IssueCatalog.TITLE_DUPLICATE)
            .Concat(Groups(_descriptions, IssueCatalog.DESCRIPTION_DUPLICATE))
            .Concat(Groups(_h1s, IssueCatalog.H1_DUPLICATE, SeriesOf()))
            .Concat(Groups(_content, IssueCatalog.DUPLICATE_CONTENT))
            .Concat(PagesWithoutFingerprint == 0 ? NearDuplicates() : []);

    /// <summary>
    /// Which paginated series a page belongs to: a key shared by every page joined to it through rel=next/prev, or the
    /// page's own id when it's in none. Series keys are negative, so they never equal a page id.
    /// </summary>
    private Func<int, int> SeriesOf()
    {
        Dictionary<int, int> index = [];
        foreach (var (from, to) in _pagination)
        {
            index.TryAdd(from, index.Count);
            index.TryAdd(to, index.Count);
        }

        var series = new DisjointSets(index.Count);
        foreach (var (from, to) in _pagination)
        {
            series.Union(index[from], index[to]);
        }

        return id => index.TryGetValue(id, out var member) ? -1 - series.Find(member) : id;
    }

    private static void AddText(List<(ulong Hash, int Id)> entries, string? text, int id)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Case and spacing don't make two titles different to a reader.
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        entries.Add((XxHash3.HashToUInt64(MemoryMarshal.AsBytes(normalized.AsSpan())), id));
    }

    /// <param name="seriesOf">
    /// For a signal that pages of one series share by design: only pages outside a page's own series count against it.
    /// </param>
    private static IEnumerable<IssueHit> Groups(List<(ulong Hash, int Id)> entries, IssueDefinition issue, Func<int, int>? seriesOf = null)
    {
        entries.Sort();
        for (var start = 0; start < entries.Count;)
        {
            var end = start + 1;
            while (end < entries.Count && entries[end].Hash == entries[start].Hash)
            {
                end++;
            }

            if (end - start == 1)
            {
                start = end;
                continue;
            }

            // Without series, every page is a series of its own.
            var keys = new int[end - start];
            Dictionary<int, int> members = [];
            var firstOfAnotherSeries = -1;
            for (var index = start; index < end; index++)
            {
                var key = keys[index - start] = seriesOf?.Invoke(entries[index].Id) ?? entries[index].Id;
                members[key] = members.GetValueOrDefault(key) + 1;
                if (firstOfAnotherSeries < 0 && key != keys[0])
                {
                    firstOfAnotherSeries = index;
                }
            }

            for (var index = start; index < end; index++)
            {
                var key = keys[index - start];
                var others = end - start - members[key];
                if (others > 0)
                {
                    var related = entries[key == keys[0] ? firstOfAnotherSeries : start].Id;
                    yield return new IssueHit(issue, entries[index].Id, RuleText.Count(others, "other page", "other pages"), related);
                }
            }

            start = end;
        }
    }

    /// <summary>
    /// Pages whose fingerprints are within <see cref="AnalysisSettings.NearDuplicateMaxBits"/> of each other, of all 256
    /// bits, joined into groups, so a cluster of near copies costs one entry per page rather than one per pair. Exact
    /// copies are left to duplicate-content.
    /// </summary>
    private IEnumerable<IssueHit> NearDuplicates()
    {
        var count = _fingerprints.Count;
        var groups = GroupNearDuplicates();

        // Each page points at the lowest-id other member of its group.
        Dictionary<int, (int Lowest, int Second)> lowest = [];
        for (var index = 0; index < count; index++)
        {
            var root = groups.Find(index);
            var id = _fingerprints[index].UrlId;
            lowest[root] = lowest.TryGetValue(root, out var current)
                ? id < current.Lowest ? (id, current.Lowest) : (current.Lowest, Math.Min(current.Second, id))
                : (id, int.MaxValue);
        }

        for (var index = 0; index < count; index++)
        {
            var root = groups.Find(index);
            var size = groups.Size(root);
            if (size < 2)
            {
                continue;
            }

            var id = _fingerprints[index].UrlId;
            var (lowestId, secondId) = lowest[root];
            yield return new IssueHit(IssueCatalog.NEAR_DUPLICATE_CONTENT, id, RuleText.Count(size - 1, "similar page", "similar pages"),
                id == lowestId ? secondId : lowestId);
        }
    }

    /// <summary>
    /// Finds every pair within k bits by pigeonhole, twice over. Within k bits of 256, one of the four lanes is within
    /// r = k / 4 of its 64; and within r bits, a lane cut into r + 2 blocks agrees exactly on two of them. So every such
    /// pair shares a bucket when the pages are sorted by one pair of blocks of one lane: 28 pairs of 8-bit blocks per lane
    /// at the default k = 24. Each of those keys is sorted and compared in turn, holding one key's arrays at a time. Pages
    /// sharing a key are compared if their lane is within r, then joined if all 256 bits are within k. That finds every
    /// pair, however big the crawl and in whatever order it was made, except in a bucket fuller than unrelated pages ever
    /// make one (see <see cref="MIN_FULL_BUCKET"/>), where each page meets only its nearest by the lane. Such a bucket holds
    /// pages sharing much of their text, and near copies in it still form one group, each joined through its neighbours.
    /// So the whole search is at most 112 keys × pages × max(64, 2 × pages / 65,536) comparisons at the default.
    /// </summary>
    private DisjointSets GroupNearDuplicates()
    {
        var count = _fingerprints.Count;
        var groups = new DisjointSets(count);
        var laneRadius = settings.NearDuplicateMaxBits / ContentFingerprint.LANES;
        var blocks = laneRadius + 2;
        var values = new ulong[count];
        var members = new int[count];
        var ends = new int[1 << Math.Min(2 * Enumerable.Range(0, blocks).Max(block => Block(block, blocks).Width), MAX_KEY_BITS)];
        for (var lane = 0; lane < ContentFingerprint.LANES; lane++)
        {
            for (var first = 0; first < blocks; first++)
            {
                for (var second = first + 1; second < blocks; second++)
                {
                    var buckets = SortByBlocks(lane, Block(first, blocks), Block(second, blocks), values, members, ends);
                    var fullBucket = Math.Max(MIN_FULL_BUCKET, 4 * (count / buckets));
                    for (int bucket = 0, start = 0; bucket < buckets; start = ends[bucket++])
                    {
                        CompareBucket(values, members, start, ends[bucket], laneRadius, fullBucket, groups);
                    }
                }
            }
        }

        return groups;
    }

    /// <summary>Where a block of a lane cut into <paramref name="blocks"/> starts, and its width: 64 / blocks bits, give or take one.</summary>
    private static (int Shift, int Width) Block(int block, int blocks) => (block * 64 / blocks, (block + 1) * 64 / blocks - block * 64 / blocks);

    /// <summary>
    /// Counting-sorts the pages by two blocks of one lane: <paramref name="values"/> gets the lane's values and
    /// <paramref name="members"/> the pages, in page order within a bucket, and <paramref name="ends"/> where each bucket
    /// ends. Returns how many buckets there are.
    /// </summary>
    private int SortByBlocks(int lane, (int Shift, int Width) first, (int Shift, int Width) second, ulong[] values, int[] members, int[] ends)
    {
        var fingerprints = CollectionsMarshal.AsSpan(_fingerprints);
        var width = first.Width + second.Width;
        var bits = Math.Min(width, MAX_KEY_BITS);
        var (firstMask, secondMask) = ((1UL << first.Width) - 1, (1UL << second.Width) - 1);
        int Bucket(ulong value)
        {
            var key = ((value >> first.Shift) & firstMask) | (((value >> second.Shift) & secondMask) << first.Width);
            return (int)(width <= MAX_KEY_BITS ? key : (key * GOLDEN_RATIO) >> (64 - MAX_KEY_BITS));
        }

        // Counted one bucket along, so after the running total each holds where its bucket starts.
        var buckets = 1 << bits;
        Array.Clear(ends, 0, buckets);
        foreach (ref readonly var fingerprint in fingerprints)
        {
            var bucket = Bucket(fingerprint.Lane(lane));
            if (bucket + 1 < buckets)
            {
                ends[bucket + 1]++;
            }
        }

        for (var bucket = 1; bucket < buckets; bucket++)
        {
            ends[bucket] += ends[bucket - 1];
        }

        // Placing each page moves its bucket's start on, so each ends up holding where its bucket ends.
        for (var index = 0; index < fingerprints.Length; index++)
        {
            var value = fingerprints[index].Lane(lane);
            var position = ends[Bucket(value)]++;
            values[position] = value;
            members[position] = index;
        }

        return buckets;
    }

    /// <summary>
    /// Compares the pages of one bucket each with every other, or in a bucket of more than <paramref name="fullBucket"/>,
    /// each with the half as many after it in the lane's order.
    /// </summary>
    private void CompareBucket(ulong[] values, int[] members, int start, int end, int laneRadius, int fullBucket, DisjointSets groups)
    {
        var fingerprints = CollectionsMarshal.AsSpan(_fingerprints);
        var size = end - start;
        var neighbours = size - 1;
        var gathered = size > fullBucket;
        if (gathered)
        {
            // Near copies met under an earlier key are one group already, and have nothing left to find.
            var root = groups.Find(members[start]);
            var joined = true;
            for (var position = start + 1; position < end && joined; position++)
            {
                joined = groups.Find(members[position]) == root;
            }

            if (joined)
            {
                return;
            }

            // Sorted by the lane, pages agreeing on more of its leading bits sit nearer one another. Many of them share much
            // of their text, so their lanes are often close: their whole fingerprints are gathered first, rather than fetched
            // from all over for each such pair.
            Array.Sort(values, members, start, size);
            neighbours = fullBucket / 2;
            if (_bucket.Length < size)
            {
                _bucket = new PageFingerprint[Math.Max(size, 2 * _bucket.Length)];
            }

            for (var position = 0; position < size; position++)
            {
                _bucket[position] = fingerprints[members[start + position]];
            }
        }

        for (var first = start; first < end - 1; first++)
        {
            var value = values[first];
            var last = Math.Min(end, first + 1 + neighbours);
            Comparisons += last - first - 1;
            for (var second = first + 1; second < last; second++)
            {
                if (BitOperations.PopCount(value ^ values[second]) <= laneRadius)
                {
                    Join(members, start, first, second, gathered, groups);
                }
            }
        }
    }

    /// <summary>
    /// Joins two pages of a bucket, at those places in its order, when all 256 bits are close enough: from the gathered
    /// fingerprints in a bucket that has them, else from the list. Exact copies are left to duplicate-content.
    /// </summary>
    private void Join(int[] members, int start, int first, int second, bool gathered, DisjointSets groups)
    {
        var fingerprints = CollectionsMarshal.AsSpan(_fingerprints);
        ref readonly var a = ref gathered ? ref _bucket[first - start] : ref fingerprints[members[first]];
        ref readonly var b = ref gathered ? ref _bucket[second - start] : ref fingerprints[members[second]];
        if (a.ContentHash != b.ContentHash && a.Distance(b) <= settings.NearDuplicateMaxBits)
        {
            groups.Union(members[first], members[second]);
        }
    }
}
