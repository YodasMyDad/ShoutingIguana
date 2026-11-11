using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// One grid read: a reader's connection to a crawl file and a snapshot, held for that one read only. It knows whether the
/// URL summary describes the crawl, keeps what it counts while the crawl stands still (briefly while it runs), and holds
/// the sets of URL ids its conditions were read into. It can be stopped: cancelling its token interrupts the statement
/// running within milliseconds, a regular expression's matching included. A writer of this process that needs the file
/// for a moment stops it too, and the read runs again once the writer has it (<see cref="CrawlReadGate"/>).
/// </summary>
internal sealed class GridReadContext : IDisposable
{
    /// <summary>
    /// The longest a read's regular expressions may take in all, beyond which the pattern is too complex for the crawl. A
    /// pattern without backtracking is linear, and simple ones take about 0.3 µs a title: a million in 0.3 s, a tenth of
    /// this. Tests lower it.
    /// </summary>
    internal static TimeSpan RegexBudget { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Each read opens its own connection, whose page cache starts empty; mapping the file reads its pages without a copy,
    /// which a scan of a million rows feels (a third quicker). SQLite caps the map at 2 GB; the rest is read as usual.
    /// </summary>
    private const long MMAP_BYTES = 2L * 1024 * 1024 * 1024;

    /// <summary>A sort of a million rows keeps its heap in memory rather than in a temporary file.</summary>
    private const int CACHE_KIB = 65_536;

    private static readonly AsyncLocal<bool> WITHOUT_SUMMARY = new();
    private static readonly AsyncLocal<bool> WITHOUT_LINK_SUMMARY = new();

    /// <summary>
    /// For the reader benchmark: reads in this flow (this thread and what it awaits) read the file as a crawl continued
    /// since its analysis would be, without its URL summary, from the crawl's own tables, as a running crawl's grid does.
    /// Nothing about the file changes.
    /// </summary>
    internal static bool ReadWithoutSummary
    {
        get => WITHOUT_SUMMARY.Value;
        set => WITHOUT_SUMMARY.Value = value;
    }

    /// <summary>
    /// For the reader benchmark: reads in this flow read the links grid as a crawl analysed by an engine from before the
    /// link summary would be, from the links themselves. Nothing about the file changes.
    /// </summary>
    internal static bool ReadWithoutLinkSummary
    {
        get => WITHOUT_LINK_SUMMARY.Value;
        set => WITHOUT_LINK_SUMMARY.Value = value;
    }

    private readonly ReadCache? _cache;
    private readonly List<UrlIdSet> _sets = [];
    private readonly Dictionary<string, object> _huge = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _large = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _once = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Dictionary<long, long>> _talliesBySlot = [];
    private (long Links, long MaxLinkId, int BlockShift)? _linkSummary;
    private bool _linkSummaryRead;
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _patternTicks = new(StringComparer.Ordinal);
    private readonly long _regexBudgetTicks = (long)(RegexBudget.TotalSeconds * Stopwatch.Frequency);
    private CancellationTokenRegistration _interrupt;
    private long _regexTicks;
    private CrawlOptions? _options;

    // The pattern matching now, known by its bytes as SQLite gives them, and a buffer each text is decoded into.
    private string? _pattern;
    private byte[] _patternBytes = [];
    private Regex? _regex;
    private long _patternSpent;
    private char[] _text = new char[1_024];

    // The last content type judged, by its bytes.
    private byte[] _contentType = [];
    private ResourceType _contentTypeIs;

    private GridReadContext(SqliteConnection connection, SqliteTransaction transaction, string path, ReadCache? cache, CancellationToken cancellationToken)
    {
        Connection = connection;
        Transaction = transaction;
        Path = path;
        _cache = cache;
        Token = cancellationToken;
    }

    public SqliteConnection Connection { get; }

    public SqliteTransaction Transaction { get; }

    public string Path { get; }

    /// <summary>Cancelled when the read should stop: its caller gave up, or a writer wants the file.</summary>
    public CancellationToken Token { get; }

    public SummaryState Summary { get; private set; } = SummaryState.Present;

    public GridMode Mode => Summary.Available ? GridMode.Summary : GridMode.Crawl;

    public AnalysisInfo? Analysis { get; private set; }

    public CrawlStatus Status { get; private set; }

    public string StartUrl { get; private set; } = string.Empty;

    /// <summary>The crawl has the analysis's indexability, where it judged a URL differently from the crawl.</summary>
    public bool HasEffectiveIndexability { get; private set; }

    /// <summary>
    /// The last analysis stored every other URL each issue is about, and the fault some checks group by (<c>issue_targets</c>,
    /// <c>issue_faults</c>), as this engine's do (<see cref="CrawlSchema.ISSUE_TARGETS_SINCE"/>). An earlier engine's
    /// analysis is grouped as that engine grouped it: by the issues' own detail and first other URL.
    /// </summary>
    public bool HasIssueTargets { get; private set; }

    /// <summary>The links have the index by target and kind an analysis adds (<see cref="CrawlSchema.LINK_KINDS_INDEX"/>).</summary>
    public bool HasLinkKindIndex { get; private set; }

    /// <summary>The crawl's indexability reason in crawl SQL: the analysis's where it judged a URL differently.</summary>
    public string Reason => HasEffectiveIndexability ? "COALESCE(e.indexability_reason, r.indexability_reason)" : "r.indexability_reason";

    /// <summary>What counts are kept under: briefly for a crawl that's running, as its counts change from one read to the next.</summary>
    public GridStamp? Stamp { get; private set; }

    /// <summary>The pattern this read gave up on, its matches taking too long on this crawl.</summary>
    public string? RefusedPattern { get; private set; }

    /// <summary>
    /// Opens the file, runs the read on one snapshot of it, and closes it: the readers' one way in. A read a writer here
    /// stopped runs again, from a new snapshot, once the writer has the file.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="RegexTooComplexException">A regular expression took too long on the crawl's text.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static T Read<T>(string path, ReadCache? cache, CancellationToken cancellationToken, Func<GridReadContext, T> read)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = CrawlReadGate.Enter(path, cancellationToken);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Preempted);
            GridReadContext? context = null;
            try
            {
                context = Open(path, cache, stop.Token);
                return read(context);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                // However the statement stopped (interrupted, or a function told of it), the caller gave up.
                throw exception as OperationCanceledException ?? new OperationCanceledException("The read was cancelled.", exception, cancellationToken);
            }
            catch (Exception) when (lease.WasPreempted)
            {
                // A writer took the file for a moment, to switch it to WAL mode or back: read again once it has.
            }
            catch (Exception exception) when (context?.RefusedPattern is { } pattern && exception is not RegexTooComplexException)
            {
                throw new RegexTooComplexException(pattern, exception);
            }
            finally
            {
                context?.Dispose();
            }
        }
    }

    /// <summary>
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
        cancellationToken.ThrowIfCancellationRequested();
        var connection = CrawlDatabase.OpenReader(path);
        SqliteTransaction? transaction = null;
        GridReadContext? context = null;
        try
        {
            CrawlDatabase.Execute(connection, $"PRAGMA mmap_size = {MMAP_BYTES}; PRAGMA cache_size = -{CACHE_KIB};");
            transaction = connection.BeginTransaction(deferred: true);
            context = new GridReadContext(connection, transaction, path, cache, cancellationToken);
            ReaderFunctions.Register(connection, context);

            // Interrupting a connection stops the statement it's running (and any it starts before that one ends); the
            // registration ends before the connection closes, so it never interrupts a closed one.
            context._interrupt = cancellationToken.Register(static state => raw.sqlite3_interrupt(((SqliteConnection)state!).Handle), connection);
            context.ReadState();
            return context;
        }
        catch
        {
            if (context is not null)
            {
                context.Dispose();
            }
            else
            {
                transaction?.Dispose();
                connection.Dispose();
            }

            throw;
        }
    }

    /// <summary>A count, kept while the crawl stands still: per file, crawl stamp, analysis and mode.</summary>
    public long Count(string key, Func<long> count) => Cached(key, count);

    public T Cached<T>(string key, Func<T> read) where T : notnull =>
        Stamp is { } stamp && _cache is { } cache ? cache.GetOrAdd($"{stamp}|{key}", read, MaxAge(stamp), Token) : read();

    /// <summary>
    /// As <see cref="Cached{T}"/>, for something large (an id set), of which only a few are kept. Without a cache, it's kept
    /// for this read, which may count with it and then page through it.
    /// </summary>
    public T CachedLarge<T>(string key, Func<T> read) where T : notnull
    {
        if (Stamp is { } stamp && _cache is { } cache)
        {
            return cache.GetOrAddLarge($"{stamp}|{key}", read, MaxAge(stamp), Token);
        }

        if (!_large.TryGetValue(key, out var kept))
        {
            _large[key] = kept = read();
        }

        return (T)kept;
    }

    /// <summary>
    /// As <see cref="Cached{T}"/>, for what the links grid keeps of a filter set, megabytes each, of which few are kept.
    /// Without a cache, it's kept for this read, which counts with it and then pages through it.
    /// </summary>
    public T CachedHuge<T>(string key, Func<T> read) where T : notnull
    {
        if (Stamp is { } stamp && _cache is { } cache)
        {
            return cache.GetOrAddHuge($"{stamp}|{key}", read, MaxAge(stamp), Token);
        }

        if (!_huge.TryGetValue(key, out var kept))
        {
            _huge[key] = kept = read();
        }

        return (T)kept;
    }

    /// <summary>
    /// As <see cref="Cached{T}"/>, for a crawl's URLs and titles held in memory for searching (<see cref="Models.SearchText"/>):
    /// megabytes for a large crawl, so only <see cref="ReadCache.TEXT_CAPACITY"/> are kept. Without a cache, it's read each time.
    /// </summary>
    public T CachedText<T>(string key, Func<T> read) where T : notnull =>
        Stamp is { } stamp && _cache is { } cache ? cache.GetOrAddText($"{stamp}|{key}", read, MaxAge(stamp), Token) : read();

    /// <summary>Whether one of those is kept for the crawl as it stands (<see cref="CachedText{T}"/> would give it without reading).</summary>
    public bool HasText(string key) => Stamp is { } stamp && _cache is { } cache && cache.HasText($"{stamp}|{key}", MaxAge(stamp));

    /// <summary>Whether one of those is kept already, read by an earlier read, or earlier in this one.</summary>
    public bool HasHuge(string key) =>
        Stamp is { } stamp && _cache is { } cache ? cache.HasHuge($"{stamp}|{key}", MaxAge(stamp)) : _huge.ContainsKey(key);

    /// <summary>Keeps one of those, read while reading something else.</summary>
    public void KeepHuge<T>(string key, T value) where T : notnull
    {
        if (Stamp is { } stamp && _cache is { } cache)
        {
            cache.SetHuge($"{stamp}|{key}", value);
        }
        else
        {
            _huge[key] = value;
        }
    }

    /// <summary>
    /// Something this read reads at most once and then holds on to, whatever a cache shared with other reads does
    /// meanwhile (a value there can be counted again, by a read of a later moment of a running crawl): the same object
    /// for the rest of the read, so what the read answers with is counted once.
    /// </summary>
    public T Once<T>(string key, Func<T> read) where T : notnull
    {
        if (!_once.TryGetValue(key, out var held))
        {
            _once[key] = held = read();
        }

        return (T)held;
    }

    /// <summary>Holds on to a value this read read some other way, as <see cref="Once"/> would have.</summary>
    public void Hold<T>(string key, T value) where T : notnull => _once[key] = value;

    /// <summary>Lets go of everything the read held on to (<see cref="Once"/>): it starts over.</summary>
    public void StartOver() => _once.Clear();

    /// <summary>Keeps a value for the reads that follow, in place of any kept under the key.</summary>
    public void Keep<T>(string key, T value) where T : notnull
    {
        if (Stamp is { } stamp)
        {
            _cache?.Set($"{stamp}|{key}", value);
        }
    }

    /// <summary>Notes something about the crawl as it stands, for the reads that follow (<see cref="Noted"/>).</summary>
    public void Note(string key)
    {
        if (Stamp is { } stamp)
        {
            _cache?.Set($"{stamp}|{key}", true);
        }
    }

    /// <summary>Whether an earlier read noted this of the crawl as it stands.</summary>
    public bool Noted(string key) => Stamp is { } stamp && _cache?.Has($"{stamp}|{key}", MaxAge(stamp)) == true;

    /// <summary>Whether a value is kept under the key for the crawl as it stands (<see cref="Cached{T}"/> would give it without reading).</summary>
    public bool Keeps(string key) => Noted(key);

    /// <summary>Whether something large is kept already, read by an earlier read.</summary>
    public bool HasLarge(string key) => Stamp is { } stamp && _cache is { } cache && cache.HasLarge($"{stamp}|{key}", MaxAge(stamp));

    public SqliteCommand Command(string sql, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        // Between statements, where an interrupt has nothing to stop.
        Token.ThrowIfCancellationRequested();
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            if (!command.Parameters.Contains(name))
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }

    public long Scalar(string sql, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        using var command = Command(sql, parameters);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    /// <summary>The slot of a set of URL ids, which this read's SQL tests rows against with <c>si_ids(slot, id)</c> or fills with <c>si_add(slot, id)</c>.</summary>
    public int Use(UrlIdSet set)
    {
        var slot = _sets.IndexOf(set);
        if (slot < 0)
        {
            _sets.Add(set);
            slot = _sets.Count - 1;
        }

        return slot;
    }

    public bool HasId(int slot, long id) => slot >= 0 && slot < _sets.Count && _sets[slot].Contains(id);

    public void AddId(int slot, long id)
    {
        if (slot >= 0 && slot < _sets.Count)
        {
            _sets[slot].Add(id);
        }
    }

    /// <summary>
    /// Whether a regular expression matches a text, both UTF-8 as SQLite holds them, for <c>si_regex</c>. Each match is
    /// stopped after <see cref="ReaderFunctions.MATCH_TIMEOUT"/>, and this read's matches after <see cref="RegexBudget"/>
    /// in all: the pattern that took longest is then refused as too complex for this crawl (and kept refused while it
    /// stands still).
    /// </summary>
    public bool Matches(ReadOnlySpan<byte> pattern, ReadOnlySpan<byte> text)
    {
        Token.ThrowIfCancellationRequested();
        if (_regex is null || !pattern.SequenceEqual(_patternBytes))
        {
            Switch(pattern);
        }

        var decoded = Decode(text);
        var started = Stopwatch.GetTimestamp();
        bool matched;
        try
        {
            matched = _regex!.IsMatch(decoded);
        }
        catch (RegexMatchTimeoutException)
        {
            Refuse(_pattern!);
            throw;
        }

        var spent = Stopwatch.GetTimestamp() - started;
        _patternSpent += spent;
        _regexTicks += spent;
        if (_regexTicks > _regexBudgetTicks)
        {
            _patternTicks[_pattern!] += _patternSpent;
            _patternSpent = 0;
            Refuse(_patternTicks.MaxBy(entry => entry.Value).Key);
        }

        return matched;
    }

    /// <summary>What a URL is by its extension, for <c>si_resource_type</c>: its text UTF-8, as SQLite holds it.</summary>
    public ResourceType TypeOfUrl(ReadOnlySpan<byte> url) => ResourceTypes.OfUrl(Decode(url));

    /// <summary>What a successful response with this content type is; rows mostly share a few, so the last is kept.</summary>
    public ResourceType TypeOfContent(ReadOnlySpan<byte> contentType)
    {
        if (!contentType.SequenceEqual(_contentType))
        {
            (_contentType, _contentTypeIs) = (contentType.ToArray(), ResourceTypes.OfContentType(Decode(contentType)));
        }

        return _contentTypeIs;
    }

    /// <summary>The rows' sort keys and URL ids a statement kept (<c>si_keep</c>), for a page selected in memory.</summary>
    public List<(long? Key, long Id)> Kept { get; } = [];

    public void Keep(long? key, long id) => Kept.Add((key, id));

    /// <summary>How many rows a statement counted with each value (<c>si_tally</c>), as a GROUP BY would without sorting them.</summary>
    public Dictionary<long, long> Tallies { get; } = [];

    public void Tally(long value) => CollectionsMarshal.GetValueRefOrAddDefault(Tallies, value, out _)++;

    /// <summary>
    /// How many rows a statement counted with each value in one of several tallies (<c>si_tally(slot, value)</c>), where
    /// one pass counts its rows several ways at once; empty for a slot nothing was counted in.
    /// </summary>
    public Dictionary<long, long> TalliesIn(int slot)
    {
        if (!_talliesBySlot.TryGetValue(slot, out var tallies))
        {
            _talliesBySlot[slot] = tallies = [];
        }

        return tallies;
    }

    public void Tally(int slot, long value) => CollectionsMarshal.GetValueRefOrAddDefault(TalliesIn(slot), value, out _)++;

    /// <summary>The ids of the links a statement is reading (<c>si_link</c>), in the order it reads them.</summary>
    public LinkIdSet? LinkIds { get; set; }

    /// <summary>The links a statement is counting by target and shape (<c>si_link</c>, <c>si_links</c>).</summary>
    public LinkTally? LinkTally { get; set; }

    /// <summary>The URLs and titles a statement is reading for searching (<c>si_search_url</c>, <c>si_search_title</c>).</summary>
    public SearchText? SearchText { get; set; }

    /// <summary>The pages whose robots directives say nofollow, which a link read from the links themselves is judged followed by.</summary>
    public UrlIdSet? NofollowPages { get; set; }

    /// <summary>One link read from <c>links</c>: its id kept, and the link counted, as the statement set up.</summary>
    public void ReadLink(long id, long toId, long fromId, long kind, long position, long flags)
    {
        LinkIds?.Add(id);
        LinkTally?.Add(toId, kind, position, flags, NofollowPages?.Contains(fromId) == true, 1);
    }

    /// <summary>
    /// The link summary an analysis stored, when it counts the crawl as it stands: its tables are this engine's, and carry
    /// the crawl row's stamp, which every run of the crawl moves on. It needs no URL summary beside it: it counts only the
    /// links, which no analysis changes. With it, how many links the crawl has, their highest id, and the size of the blocks
    /// they're counted by; null when the links grid must read the links themselves.
    /// </summary>
    public (long Links, long MaxLinkId, int BlockShift)? LinkSummary
    {
        get
        {
            if (!_linkSummaryRead)
            {
                _linkSummaryRead = true;
                _linkSummary = ReadLinkSummary();
            }

            return _linkSummary;
        }
    }

    /// <summary>The options the crawl ran with, read once; an option its engine didn't have takes its default.</summary>
    public CrawlOptions Options => _options ??= ReadOptions();

    /// <summary>The name the crawl's robots meta tags and headers could address the crawler by.</summary>
    public string RobotsToken => UserAgents.Resolve(Options.UserAgent, Options.CustomUserAgent).RobotsToken;

    public void Dispose()
    {
        // First, so no interrupt can reach a connection that has closed.
        _interrupt.Dispose();
        Transaction.Dispose();
        Connection.Dispose();
    }

    private static TimeSpan? MaxAge(GridStamp stamp) => stamp.Live ? ReadCache.LIVE_FOR : null;

    /// <summary>UTF-8 text as characters, in a buffer reused for every row: valid until the next call.</summary>
    private ReadOnlySpan<char> Decode(ReadOnlySpan<byte> text)
    {
        // UTF-8 never takes fewer bytes than UTF-16 takes characters.
        if (text.Length > _text.Length)
        {
            _text = new char[Math.Max(text.Length, _text.Length * 2)];
        }

        return _text.AsSpan(0, Encoding.UTF8.GetChars(text, _text));
    }

    /// <summary>Matching moves on to another pattern: compiled once a read, unless it was refused for this crawl already.</summary>
    private void Switch(ReadOnlySpan<byte> pattern)
    {
        if (_pattern is not null)
        {
            _patternTicks[_pattern] += _patternSpent;
            _patternSpent = 0;
        }

        var text = Encoding.UTF8.GetString(pattern);
        if (!_patterns.TryGetValue(text, out var regex))
        {
            if (Stamp is { } stamp && _cache?.Has($"{stamp}|refused|{text}", MaxAge(stamp)) == true)
            {
                Refuse(text);
            }

            _patterns[text] = regex = ReaderFunctions.Compile(text);
            _patternTicks[text] = 0;
        }

        (_pattern, _regex, _patternBytes) = (text, regex, pattern.ToArray());
    }

    /// <summary>Gives up on a pattern: the statement fails, and the read says why (<see cref="RefusedPattern"/>).</summary>
    private void Refuse(string pattern)
    {
        RefusedPattern = pattern;
        if (Stamp is { } stamp)
        {
            _cache?.Set($"{stamp}|refused|{pattern}", true);
        }

        throw new RegexTooComplexException(pattern);
    }

    private (long Links, long MaxLinkId, int BlockShift)? ReadLinkSummary()
    {
        // A crawl that's running has links its summary, if it has one, doesn't count yet: its stamp moved when the run
        // began, and whatever a file says of itself, a crawl that's running is read from its links.
        Token.ThrowIfCancellationRequested();
        return ReadWithoutLinkSummary || Stamp is { Live: true } ? null : LinkSummaryBuilder.ReadCurrent(Connection, Transaction);
    }

    private CrawlOptions ReadOptions()
    {
        using var command = Command("SELECT options_json FROM crawl WHERE id = 1", []);
        try
        {
            return command.ExecuteScalar() is string json
                ? StoredJson.Deserialize(json, EngineJsonContext.Default.CrawlOptions, new CrawlOptions { StartUrl = StartUrl }) ?? new CrawlOptions { StartUrl = StartUrl }
                : new CrawlOptions { StartUrl = StartUrl };
        }
        catch (JsonException)
        {
            // Options edited by hand: the engine's defaults are the best guess at how it ran.
            return new CrawlOptions { StartUrl = StartUrl };
        }
    }

    /// <summary>
    /// The crawl row, the last analysis, and whether the summary describes the crawl, from the snapshot. Why not, when it
    /// doesn't, is the first that applies: the crawl is running, was never analysed, was analysed by another engine, or
    /// was continued since.
    /// </summary>
    private void ReadState()
    {
        using (var command = Command("SELECT status, updated_at, start_url FROM crawl WHERE id = 1", []))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new InvalidOperationException("This file is not a crawl database.");
            }

            Status = (CrawlStatus)reader.GetInt32(0);
            var updatedAt = reader.GetInt64(1);
            StartUrl = reader.GetString(2);
            Analysis = IssueReader.ReadAnalysis(Connection, Transaction);
            if (ReadWithoutSummary || !UrlSummaryReader.IsAvailable(Connection, Transaction))
            {
                Summary = new SummaryState(false, UnavailableReason());
            }

            HasEffectiveIndexability = IssueReader.HasTable(Connection, Transaction, "effective_indexability");
            HasIssueTargets = Analysis?.AnalysisVersion >= CrawlSchema.ISSUE_TARGETS_SINCE && IssueReader.HasTable(Connection, Transaction, "issue_targets") &&
                              IssueReader.HasTable(Connection, Transaction, "issue_faults");
            HasLinkKindIndex = IndexColumns("ix_links_to_kind") == "links:to_id,kind";

            // A crawl running here or elsewhere changes from one read to the next; one interrupted stands still until it's
            // continued, which stamps it again.
            Stamp = new GridStamp(Path, updatedAt, Analysis?.AnalyzedAt.ToUnixTimeMilliseconds(), Mode, Live: Summary.Reason == SummaryUnavailableReason.Crawling);
        }
    }

    /// <summary>"table:column,column" of an index, so one another program made its own way isn't taken for the engine's.</summary>
    private string IndexColumns(string index)
    {
        using var command = Command("""
            SELECT m.tbl_name || ':' || (SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info($index) ORDER BY seqno))
            FROM sqlite_master m WHERE m.type = 'index' AND m.name = $index
            """, [new("$index", index)]);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private SummaryUnavailableReason UnavailableReason()
    {
        // A file that says it's running is interrupted unless someone holds its lock.
        if (Status == CrawlStatus.Running && CrawlDatabase.IsInUse(Path))
        {
            return SummaryUnavailableReason.Crawling;
        }

        if (Analysis is null)
        {
            return SummaryUnavailableReason.NotAnalyzed;
        }

        return Analysis.HasUrlSummary && IssueReader.HasTable(Connection, Transaction, "url_summary")
            ? SummaryUnavailableReason.CrawlChanged
            : SummaryUnavailableReason.EngineChanged;
    }
}
