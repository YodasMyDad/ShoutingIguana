using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Titles, descriptions, H1s and main content shared between indexable pages. Each page contributes a 64-bit hash per
/// signal, so memory stays at a few bytes per page, and groups are found by sorting rather than comparing pairs.
/// </summary>
internal sealed class DuplicateRules(AnalysisSettings settings) : ISiteRule
{
    /// <summary>Bounds the work in a bucket of pages that share a fingerprint block (thousands of templated pages).</summary>
    private const int MAX_NEIGHBOURS = 1_000;

    private readonly List<(ulong Hash, int Id)> _titles = [];
    private readonly List<(ulong Hash, int Id)> _descriptions = [];
    private readonly List<(ulong Hash, int Id)> _h1s = [];
    private readonly List<(ulong Hash, int Id)> _content = [];
    private readonly List<(ulong SimHash, ulong ContentHash, int Id)> _fingerprints = [];

    public void Observe(AnalyzedPage page)
    {
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
            _fingerprints.Add((facts.SimHash, facts.ContentHash, page.UrlId));
        }
    }

    public IEnumerable<IssueHit> Finish() =>
        Groups(_titles, IssueCatalog.TITLE_DUPLICATE)
            .Concat(Groups(_descriptions, IssueCatalog.DESCRIPTION_DUPLICATE))
            .Concat(Groups(_h1s, IssueCatalog.H1_DUPLICATE))
            .Concat(Groups(_content, IssueCatalog.DUPLICATE_CONTENT))
            .Concat(NearDuplicates());

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

    private static IEnumerable<IssueHit> Groups(List<(ulong Hash, int Id)> entries, IssueDefinition issue)
    {
        entries.Sort();
        for (var start = 0; start < entries.Count;)
        {
            var end = start + 1;
            while (end < entries.Count && entries[end].Hash == entries[start].Hash)
            {
                end++;
            }

            var others = end - start - 1;
            for (var index = start; others > 0 && index < end; index++)
            {
                var related = entries[index == start ? start + 1 : start].Id;
                yield return new IssueHit(issue, entries[index].Id, RuleText.Count(others, "other page", "other pages"), related);
            }

            start = end;
        }
    }

    /// <summary>
    /// Pigeonhole: fingerprints within k bits of each other agree exactly on at least one of k + 1 blocks. Pages are
    /// bucketed by each block in turn and compared only within buckets. Matches are joined into groups, so a cluster
    /// of near copies costs one entry per page rather than one per pair. Exact copies are left to duplicate-content.
    /// </summary>
    private IEnumerable<IssueHit> NearDuplicates()
    {
        var count = _fingerprints.Count;
        var maxDistance = settings.NearDuplicateMaxDistance;
        var groups = new DisjointSets(count);
        var blocks = maxDistance + 1;
        var keys = new (ulong Block, int Index)[count];
        for (var block = 0; block < blocks; block++)
        {
            var shift = block * 64 / blocks;
            var width = (block + 1) * 64 / blocks - shift;
            var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
            for (var index = 0; index < count; index++)
            {
                keys[index] = ((_fingerprints[index].SimHash >> shift) & mask, index);
            }

            Array.Sort(keys);
            for (var first = 0; first < count; first++)
            {
                for (var second = first + 1; second < count && second - first <= MAX_NEIGHBOURS && keys[second].Block == keys[first].Block; second++)
                {
                    var (a, b) = (_fingerprints[keys[first].Index], _fingerprints[keys[second].Index]);
                    if (a.ContentHash != b.ContentHash && BitOperations.PopCount(a.SimHash ^ b.SimHash) <= maxDistance)
                    {
                        groups.Union(keys[first].Index, keys[second].Index);
                    }
                }
            }
        }

        // Each page points at the lowest-id other member of its group.
        Dictionary<int, (int Lowest, int Second)> lowest = [];
        for (var index = 0; index < count; index++)
        {
            var root = groups.Find(index);
            var id = _fingerprints[index].Id;
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

            var id = _fingerprints[index].Id;
            var (lowestId, secondId) = lowest[root];
            yield return new IssueHit(IssueCatalog.NEAR_DUPLICATE_CONTENT, id, RuleText.Count(size - 1, "similar page", "similar pages"),
                id == lowestId ? secondId : lowestId);
        }
    }
}
