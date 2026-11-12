using System.Globalization;
using ShoutingIguana.Cli.Benchmarking.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Holds the passes over every link that a host with cores to spare splits over several connections to what they are
/// for: being split, and being faster for it. A time gate alone can't: every one of those passes takes less than the
/// <c>link-pass-split</c> gate on one connection too, so a pass that silently stopped splitting would pass it. Each split
/// case is held to two things, from the same run:
/// <list type="bullet">
/// <item>its passes were split: every pass it read reported more than one connection (<c>LinkGridReader.Split</c>);</item>
/// <item>on a machine of 4 cores or more (3 helpers or more), it took at most <see cref="MAX_SHARE"/> of the time its
/// twin on one connection took, each at its slowest page's p50. Split in four, a pass takes about a quarter.</item>
/// </list>
/// </summary>
internal static class SplitGate
{
    /// <summary>What names a case read split over several connections; its twin on one connection has <see cref="ONE"/> in its place.</summary>
    public const string SPLIT = "(every link read, on several connections)";

    public const string ONE = "(every link read)";

    /// <summary>The most a split pass may take of its one-connection twin's time.</summary>
    public const double MAX_SHARE = 0.6;

    /// <summary>The cores from which the time is held to: three helpers and the read's own connection.</summary>
    public const int TIMED_FROM_CORES = 4;

    /// <summary>The failures, one line each; none when every split case was split, and faster for it where that's held.</summary>
    /// <param name="connections">The connections each pass of each split case reported, by case name.</param>
    public static List<string> Check(IReadOnlyList<ReaderBenchCase> cases, IReadOnlyDictionary<string, IReadOnlyList<int>> connections, int cores)
    {
        List<string> failures = [];
        var byName = cases.ToDictionary(read => read.Name, StringComparer.Ordinal);
        foreach (var split in cases.Where(read => read.Name.Contains(SPLIT, StringComparison.Ordinal)))
        {
            var reported = connections.GetValueOrDefault(split.Name) ?? [];
            if (reported.Count == 0 || reported.All(count => count <= 1))
            {
                failures.Add($"{split.Name}: no pass was split, every one ran on one connection");
                continue;
            }

            if (reported.Any(count => count <= 1))
            {
                failures.Add($"{split.Name}: {reported.Count(count => count <= 1)} of its {reported.Count} passes ran on one connection");
            }

            if (cores >= TIMED_FROM_CORES && byName.GetValueOrDefault(split.Name.Replace(SPLIT, ONE, StringComparison.Ordinal)) is { } one)
            {
                var (splitMs, oneMs) = (SlowestP50(split), SlowestP50(one));
                if (oneMs > 0 && splitMs > MAX_SHARE * oneMs)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{split.Name}: {splitMs:N1} ms at p50, {splitMs / oneMs:P0} of the {oneMs:N1} ms on one connection, over the {MAX_SHARE:P0} a split pass may take"));
                }
            }
        }

        return failures;
    }

    /// <summary>The p50 of its slowest page: where its pass is, for a read whose first page needs none.</summary>
    private static double SlowestP50(ReaderBenchCase read) => Math.Max(read.FirstP50Ms, Math.Max(read.HalfP50Ms ?? 0, read.PastHalfP50Ms ?? 0));
}
