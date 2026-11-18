using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The writer's state for a recheck's run: what is left to hand out, what each URL handed out is to the recheck (part of
/// its scope, or a URL fetched one step from it), how the judged URLs answered the first time, and the URLs that lost a
/// link, for the let-go walk at the end. Used only on the writer's loop.
/// </summary>
internal sealed class RecheckRun(RecheckPlan plan)
{
    private readonly Queue<FrontierItem> _internal = new();
    private readonly Queue<FrontierItem> _external = new();

    // The step each URL handed out is at: 0 for the recheck's scope (and the redirects it follows), 1 for a URL a page in
    // its scope led to, whose own links are recorded and not followed.
    private readonly Dictionary<int, int> _steps = [];
    private readonly Dictionary<int, (FetchOutcome Outcome, int? StatusCode)> _firstAnswers = [];

    // How the site last answered each URL handed out.
    private readonly Dictionary<int, RecheckAnswer> _answers = [];
    private int _done;
    private int _total;
    private int _phase = 1;

    public RecheckPlan Plan { get; } = plan;

    public IssueDefinition? Check { get; } = plan.CheckId is { } id ? IssueCatalog.Find(id) : null;

    /// <summary>Its number in the file, given as it opens.</summary>
    public int Id { get; set; }

    /// <summary>When it began, in milliseconds since 1970: a URL fetched since was fetched by it.</summary>
    public long StartedAt { get; set; }

    /// <summary>URLs that lost a link to them while it ran: where the let-go walk starts.</summary>
    public HashSet<int> LostLinks { get; } = [];

    /// <summary>The sitemaps it read again, by id: those the crawl had and nothing lists now go once the reading is done.</summary>
    public HashSet<int> SitemapsRead { get; } = [];

    /// <summary>The URLs the crawl didn't have that it found and handed out, one step from its scope.</summary>
    public HashSet<int> Found { get; } = [];

    /// <summary>The URLs it fetched, so a redirect is followed into each once.</summary>
    public HashSet<int> Fetched { get; } = [];

    /// <summary>Its run has said how it ended: from here on, nothing it stores waits for more.</summary>
    public bool Ended { get; set; }

    /// <summary>It failed, and was rolled back whole: nothing more is stored.</summary>
    public bool Discarded { get; set; }

    public int Phase => Volatile.Read(ref _phase);

    public RecheckProgress Progress => new(Volatile.Read(ref _phase), Volatile.Read(ref _done), Volatile.Read(ref _total));

    public int Done => Volatile.Read(ref _done);

    public int StepOf(int id) => _steps.GetValueOrDefault(id);

    /// <summary>How the site answered a URL it judges: never asked, unless a fetch of it came back.</summary>
    public RecheckAnswer AnswerOf(int id) => _answers.GetValueOrDefault(id, RecheckAnswer.NotAsked);

    /// <summary>Hands a URL out at <paramref name="step"/>, once.</summary>
    public bool Enqueue(FrontierItem item, int step)
    {
        if (!_steps.TryAdd(item.Id, step))
        {
            return false;
        }

        (item.IsInternal ? _internal : _external).Enqueue(item);
        Interlocked.Increment(ref _total);
        return true;
    }

    public List<FrontierItem> Claim(bool isInternal, int count)
    {
        var queue = isInternal ? _internal : _external;
        List<FrontierItem> items = [];
        while (items.Count < count && queue.TryDequeue(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>A URL it handed out came back stored.</summary>
    public void Recorded(FrontierItem item, ResponseRow response)
    {
        Fetched.Add(item.Id);
        Interlocked.Increment(ref _done);
        _answers[item.Id] = RecheckAnswers.Of(response);
        if (_phase == 1)
        {
            _firstAnswers.TryAdd(item.Id, (response.Outcome, response.StatusCode));
        }
    }

    /// <summary>Moves on to phase 2, and returns the referring pages to fetch: those of the judged URLs that still fail.</summary>
    public List<RecheckScopeUrl> StartPhaseTwo()
    {
        Volatile.Write(ref _phase, 2);
        var failing = Plan.Scope.Where(url => url.Role == RecheckRole.Judged).Select(url => url.Id).Where(StillFails).ToHashSet();
        return [.. Plan.Scope.Where(url => url.Phase == 2 && url.Refers is { } refers && failing.Contains(refers) && !_steps.ContainsKey(url.Id))];
    }

    /// <summary>
    /// Whether a judged URL still fails after the first fetch, so the pages referring to it are fetched too, where the fix
    /// may be: a response-code check's URL that still doesn't answer properly; another check of a URL that answers anything
    /// but a plain success (still a redirect, still refused); and a check of a page's own address (its capitals, its
    /// scheme), which the referring pages' links decide, always. One the first fetch didn't reach still fails.
    /// </summary>
    private bool StillFails(int id)
    {
        if (!_firstAnswers.TryGetValue(id, out var answer))
        {
            return true;
        }

        return Check is null || CheckJudging.Of(Check) switch
        {
            UrlJudging.Responses => answer.Outcome is not (FetchOutcome.Success or FetchOutcome.Redirect),
            UrlJudging.Urls => answer.Outcome != FetchOutcome.Success,
            _ => true
        };
    }
}
