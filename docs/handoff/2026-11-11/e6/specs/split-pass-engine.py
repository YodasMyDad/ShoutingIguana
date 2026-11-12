E = 'src/ShoutingIguana.Engine/Storage/'

WRITES = [
(E + 'ReadHelpers.cs', '''namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The room a host has for a read to use more than one connection at once: a pass over every link of a large crawl is
/// split by link id over several, each on a core of its own (<see cref="LinkGridReader"/>). A read takes only the helpers
/// that are free the moment it asks, never waiting for one, and gives them back as its pass ends; with none free, or none
/// given, it reads on its own connection as it always did, and answers the same.
/// </summary>
/// <param name="tryTake">Takes one helper if one is free right now: true when it did. Never waits.</param>
/// <param name="release">Gives one helper back.</param>
public sealed class ReadHelpers(Func<bool> tryTake, Action release)
{
    /// <summary>Up to this many helpers at once, shared by every read that's given these: for a host with no queue of reads of its own.</summary>
    public static ReadHelpers UpTo(int most)
    {
        if (most <= 0)
        {
            return new ReadHelpers(() => false, () => { });
        }

        SemaphoreSlim free = new(most, most);
        return new ReadHelpers(() => free.Wait(0), () => free.Release());
    }

    /// <summary>As many helpers as are free now, up to <paramref name="most"/>: given back when the result is disposed.</summary>
    internal Borrowed Borrow(int most)
    {
        var taken = 0;
        while (taken < most && tryTake())
        {
            taken++;
        }

        return new Borrowed(taken, release);
    }

    /// <summary>The helpers one read holds for as long as its pass lasts.</summary>
    internal sealed class Borrowed(int count, Action release) : IDisposable
    {
        private int _held = count;

        public int Count { get; } = count;

        public void Dispose()
        {
            for (var held = Interlocked.Exchange(ref _held, 0); held > 0; held--)
            {
                release();
            }
        }
    }
}
'''),
]

EDITS = [
(E + 'ReadCache.cs',
'''    private readonly Store _entries = new(capacity);
''',
'''    /// <summary>
    /// The host's room for a read to use several connections at once (<see cref="ReadHelpers"/>); none unless the host
    /// gives some, and a read without any reads on one connection.
    /// </summary>
    public ReadHelpers? Helpers { get; init; }

    private readonly Store _entries = new(capacity);
'''),
(E + 'GridReadContext.cs',
'''    private static GridReadContext Open(string path, ReadCache? cache, CancellationToken cancellationToken)
    {
''',
'''    /// <summary>
    /// The helpers this read may use for a pass split over several connections: as many of <paramref name="most"/> as the
    /// host has free right now (<see cref="ReadCache.Helpers"/>), given back when the result is disposed. None for a read
    /// with no cache, or whose host gives none.
    /// </summary>
    public ReadHelpers.Borrowed? BorrowHelpers(int most) => most > 0 && _cache?.Helpers is { } helpers ? helpers.Borrow(most) : null;

    /// <summary>
    /// Another read of the same file alongside this one, on a connection of its own, for one part of a pass split between
    /// them: it sees this read's sets of URL ids in the same slots, so the same SQL runs on either, and has its own
    /// functions, its own patterns and their time. It's a read as any other to a writer here that wants the file: stopped
    /// with this one, and gone before the writer has it. Null when the file it opened isn't the file as this read has it
    /// (another program changed it in between): the pass is then this read's alone.
    /// </summary>
    /// <param name="stop">Stops it: this read's own token, or one cancelled with it.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before it opened.</exception>
    public Alongside? OpenAlongside(CancellationToken stop)
    {
        var lease = CrawlReadGate.Enter(Path, stop);
        GridReadContext? helper = null;
        try
        {
            helper = Open(Path, _cache, stop);
            if (helper.Stamp != Stamp)
            {
                helper.Dispose();
                lease.Dispose();
                return null;
            }

            helper._sets.AddRange(_sets);
            return new Alongside(helper, lease);
        }
        catch
        {
            helper?.Dispose();
            lease.Dispose();
            throw;
        }
    }

    /// <summary>A read alongside another (<see cref="OpenAlongside"/>): closed, and its place among the file's reads given up, when disposed.</summary>
    public sealed class Alongside(GridReadContext context, CrawlReadGate.ReadLease lease) : IDisposable
    {
        public GridReadContext Context { get; } = context;

        public void Dispose()
        {
            Context.Dispose();
            lease.Dispose();
        }
    }

    private static GridReadContext Open(string path, ReadCache? cache, CancellationToken cancellationToken)
    {
'''),
(E + 'Models/LinkIdSet.cs',
'''    /// <summary>Counts the ids, and readies it for <see cref="Range"/> and <see cref="Rank"/>. Nothing is added after.</summary>
''',
'''    /// <summary>
    /// Adds another set's ids, neither sealed yet: the parts of one pass split over several connections, each of which
    /// kept the ids of its own stretch of the links. Both were made for the same crawl, so both keep their ids the same way.
    /// </summary>
    public void Merge(LinkIdSet other)
    {
        if (_bits is null || other._bits is null)
        {
            _adding!.AddRange(other._adding ?? []);
            return;
        }

        for (var word = 0; word < _bits.Length && word < other._bits.Length; word++)
        {
            _bits[word] |= other._bits[word];
        }
    }

    /// <summary>Counts the ids, and readies it for <see cref="Range"/> and <see cref="Rank"/>. Nothing is added after.</summary>
'''),
(E + 'Models/LinkTally.cs',
'''    /// <summary>
    /// What a link is, as the facets count it: its kind and position, unknown when this engine has no name for them, and
''',
'''    /// <summary>Adds another tally's counts: the parts of one pass split over several connections, each of which counted its own stretch of the links.</summary>
    public void Merge(LinkTally other)
    {
        Links += other.Links;
        for (var shape = 0; shape < SHAPES; shape++)
        {
            ByShape[shape] += other.ByShape[shape];
        }

        for (var target = 0; target < _byTarget.Length && target < other._byTarget.Length; target++)
        {
            _byTarget[target] = (int)Math.Min(int.MaxValue, (long)_byTarget[target] + other._byTarget[target]);
        }
    }

    /// <summary>
    /// What a link is, as the facets count it: its kind and position, unknown when this engine has no name for them, and
'''),
(E + 'LinkGridReader.cs',
'''    /// <summary>Unknown, among the status classes a URL's facts hold.</summary>
''',
'''    /// <summary>
    /// The most helpers a pass over every link takes beside its own connection (<see cref="ReadHelpers"/>): four
    /// connections in all. Measured on the 100k benchmark's 5 million links, a pass split in four takes a quarter of the
    /// time, and one in eight a fifth to a sixth, for twice the cores kept from other reads. Tests change it.
    /// </summary>
    internal static int PassHelpers { get; set; } = 3;

    /// <summary>
    /// From this many links on, a pass over all of them is worth splitting: below it the pass takes tens of milliseconds
    /// by itself, and opening the connections to split it a few. Tests lower it.
    /// </summary>
    internal static long SplitFrom { get; set; } = 500_000;

    /// <summary>Tests only: how many connections each pass over the links ran on, in order.</summary>
    internal static Action<int>? Split { get; set; }

    /// <summary>Unknown, among the status classes a URL's facts hold.</summary>
'''),
(E + 'LinkGridReader.cs',
'''    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally, bool indexed = true)
    {
        var found = ids ? new LinkIdSet(LargestLinkId(context), total) : null;
        var counted = tally ? NewTally(context) : null;
        (context.LinkIds, context.LinkTally, context.NofollowPages) = (found, counted, tally ? NofollowPages(context) : null);
        try
        {
            // With nothing to count, the id is all that's passed on.
            var each = tally ? $"{ReaderFunctions.LINK}(l.rowid, l.to_id, l.from_id, l.kind, l.position, l.flags)" : $"{ReaderFunctions.LINK_ID}(l.rowid)";
            var sql = $"SELECT COUNT({each}) FROM {(indexed ? filters.From : "links l NOT INDEXED")}{filters.Where(Within(context))}";
            Ran?.Invoke(sql);
            using var command = context.Command(sql, filters.Parameters);
            command.ExecuteScalar();
        }
        finally
        {
            (context.LinkIds, context.LinkTally, context.NofollowPages) = (null, null, null);
        }

        return (found?.Seal(), counted);
    }
''',
'''    private static (LinkIdSet? Ids, LinkTally? Tally) Scan(GridReadContext context, Filters filters, long total, bool ids, bool tally, bool indexed = true)
    {
        var largest = LargestLinkId(context);
        LinkIdSet? NewIds() => ids ? new LinkIdSet(largest, total) : null;
        var nofollow = tally ? NofollowPages(context) : null;

        // With nothing to count, the id is all that's passed on.
        var each = tally ? $"{ReaderFunctions.LINK}(l.rowid, l.to_id, l.from_id, l.kind, l.position, l.flags)" : $"{ReaderFunctions.LINK_ID}(l.rowid)";
        var inOrder = !indexed || !filters.Direct;
        var from = inOrder ? "links l NOT INDEXED" : filters.From;

        // One part of the pass, on one read's connection: the links between two ids (all of them, with no bounds), kept
        // and counted by that read.
        (LinkIdSet? Ids, LinkTally? Tally) Part(GridReadContext read, string? bounds)
        {
            var (found, counted) = (NewIds(), tally ? NewTally(context) : null);
            (read.LinkIds, read.LinkTally, read.NofollowPages) = (found, counted, nofollow);
            try
            {
                var sql = $"SELECT COUNT({each}) FROM {from}{filters.Where(bounds is null ? Within(context) : bounds)}";
                Ran?.Invoke(sql);
                using var command = read.Command(sql, filters.Parameters);
                command.ExecuteScalar();
            }
            finally
            {
                (read.LinkIds, read.LinkTally, read.NofollowPages) = (null, null, null);
            }

            return (found, counted);
        }

        // Every link read in order, of a crawl that stands still, and many of them: split by link id over as many
        // connections as the host has room for, each on a thread of its own. A running crawl's pass stays on the one
        // connection and its one snapshot, which is the moment it answers for.
        using var helpers = inOrder && largest >= SplitFrom && context.Stamp is { Live: false } ? context.BorrowHelpers(PassHelpers) : null;
        if (helpers is not { Count: > 0 })
        {
            Split?.Invoke(1);
            var (found, counted) = Part(context, null);
            return (found?.Seal(), counted);
        }

        var parts = helpers.Count + 1;
        string Bounds(int part) => string.Create(CultureInfo.InvariantCulture, $"{(part == 0 ? string.Empty : $"l.rowid > {largest * part / parts}")}{(part is 0 || part == parts - 1 ? string.Empty : " AND ")}{(part == parts - 1 ? string.Empty : $"l.rowid <= {largest * (part + 1) / parts}")}");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
        var others = Enumerable.Range(1, helpers.Count).Select(part => Task.Factory.StartNew(() =>
        {
            try
            {
                using var alongside = context.OpenAlongside(stop.Token);
                return alongside is null ? ((LinkIdSet? Ids, LinkTally? Tally)?)null : Part(alongside.Context, Bounds(part));
            }
            catch
            {
                // One part failed, so the pass has: the others stop.
                stop.Cancel();
                throw;
            }
        }, stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        (LinkIdSet? Ids, LinkTally? Tally) mine;
        try
        {
            mine = Part(context, Bounds(0));
        }
        catch
        {
            stop.Cancel();
            Task.WaitAll(others.Select(other => other.ContinueWith(_ => { }, TaskScheduler.Default)).ToArray());
            throw;
        }

        try
        {
            Task.WaitAll(others);
        }
        catch (AggregateException failed)
        {
            // What a part failed with is what the pass fails with: a pattern too complex before the parts stopped for it.
            throw failed.Flatten().InnerExceptions.FirstOrDefault(exception => exception is not OperationCanceledException) ?? failed.InnerExceptions[0];
        }

        if (others.Any(other => other.Result is null))
        {
            // A connection found the file changed under the read: the pass again, on this read's own.
            Split?.Invoke(1);
            var (found, counted) = Part(context, null);
            return (found?.Seal(), counted);
        }

        Split?.Invoke(parts);
        foreach (var other in others.Select(other => other.Result!.Value))
        {
            if (other.Ids is not null)
            {
                mine.Ids!.Merge(other.Ids);
            }

            if (other.Tally is not null)
            {
                mine.Tally!.Merge(other.Tally);
            }
        }

        return (mine.Ids?.Seal(), mine.Tally);
    }
'''),
]
