using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Cli.Benchmarking.Models;

namespace ShoutingIguana.Cli.Tests;

/// <summary>
/// The read gate's hold on the passes split over connections: each must have been split, and on 4 cores or more be
/// faster for it than its twin on one connection, in the same run. A time gate alone passed a pass that had stopped
/// splitting, as every pass takes less than its gate on one connection too.
/// </summary>
public sealed class SplitGateTests
{
    private const string TEXT = "Links grid: text contains a common word ";
    private const string ENDS = "Link ends: the pages of every link ";

    private static readonly ReaderBenchCase[] SPLIT_AS_MEASURED =
    [
        Case(TEXT + SplitGate.ONE, 664, 700, 687),
        Case(TEXT + SplitGate.SPLIT, 113, 118, 122),
        Case(ENDS + SplitGate.ONE, 694, 698, 701),
        Case(ENDS + SplitGate.SPLIT, 148, 150, 151),
        Case(ENDS + SplitGate.SPLIT + ": the URL grid narrowed to them", 156, null, null),
        Case(ENDS + SplitGate.ONE + ": the URL grid narrowed to them", 685, null, null),

        // A read whose first page is found on an index, and whose pass is the page half way down.
        Case("Links grid: content links deep in " + SplitGate.ONE, 44, 471, 820),
        Case("Links grid: content links deep in " + SplitGate.SPLIT, 72, 232, 230)
    ];

    [Fact]
    public void PassesSplitAndFasterForItPass()
    {
        Assert.Empty(SplitGate.Check(SPLIT_AS_MEASURED, Reported(8, 8, 8, 8), cores: 32));
        Assert.Empty(SplitGate.Check(SPLIT_AS_MEASURED, Reported(4, 4, 4, 4), cores: 4));

        // A read whose first page reads no pass reports only the passes it made.
        Assert.Empty(SplitGate.Check(SPLIT_AS_MEASURED, Reported(8, 8, 8, 8).ToDictionary(entry => entry.Key, entry => (IReadOnlyList<int>)entry.Value.Take(1).ToList()), cores: 32));
    }

    /// <summary>Splitting switched off: every pass reports one connection, and takes what one connection takes.</summary>
    [Fact]
    public void PassesThatStoppedSplittingFailEachOfThem()
    {
        var unsplit = SPLIT_AS_MEASURED.Select(read => read.Name.Contains(SplitGate.SPLIT, StringComparison.Ordinal)
            ? SPLIT_AS_MEASURED.Single(one => one.Name == read.Name.Replace(SplitGate.SPLIT, SplitGate.ONE, StringComparison.Ordinal)) with { Name = read.Name }
            : read).ToArray();
        var failures = SplitGate.Check(unsplit, Reported(1, 1, 1, 1), cores: 32);
        Assert.Equal(4, failures.Count(failure => failure.EndsWith("no pass was split, every one ran on one connection", StringComparison.Ordinal)));

        // Nothing heard of a case at all is as bad: its passes weren't split.
        Assert.Equal(4, SplitGate.Check(SPLIT_AS_MEASURED, new Dictionary<string, IReadOnlyList<int>>(), cores: 32).Count);

        // Some of a case's passes on one connection: said, with how many.
        Assert.Contains("2 of its 4 passes ran on one connection", Assert.Single(SplitGate.Check(SPLIT_AS_MEASURED,
            Reported(8, 1, 8, 1).Where(entry => entry.Key.StartsWith(TEXT, StringComparison.Ordinal)).ToDictionary()
                .Concat(Reported(8, 8, 8, 8).Where(entry => !entry.Key.StartsWith(TEXT, StringComparison.Ordinal))).ToDictionary(), cores: 32)), StringComparison.Ordinal);
    }

    /// <summary>Split, but no faster for it: over the share, on 4 cores or more; on fewer, only being split is held to.</summary>
    [Fact]
    public void ASplitPassNoFasterForItFailsOnFourCoresOrMore()
    {
        var slow = SPLIT_AS_MEASURED.Select(read => read.Name == TEXT + SplitGate.SPLIT ? Case(read.Name, 300, 450, 440) : read).ToArray();
        var failure = Assert.Single(SplitGate.Check(slow, Reported(4, 4, 4, 4), cores: 4));
        Assert.StartsWith(TEXT + SplitGate.SPLIT + ": 450.0 ms at p50, ", failure, StringComparison.Ordinal);
        Assert.Contains("of the 700.0 ms on one connection", failure, StringComparison.Ordinal);
        Assert.Empty(SplitGate.Check(slow, Reported(2, 2, 2, 2), cores: 3));
    }

    private static Dictionary<string, IReadOnlyList<int>> Reported(params int[] connections) =>
        SPLIT_AS_MEASURED.Where(read => read.Name.Contains(SplitGate.SPLIT, StringComparison.Ordinal))
            .ToDictionary(read => read.Name, IReadOnlyList<int> (_) => connections, StringComparer.Ordinal);

    private static ReaderBenchCase Case(string name, double first, double? half, double? pastHalf) =>
        new(name, "links-grid", name.Contains(SplitGate.SPLIT, StringComparison.Ordinal) ? "link-pass-split" : "link-pass", 1_000, first, first, half, half, pastHalf, pastHalf);
}
