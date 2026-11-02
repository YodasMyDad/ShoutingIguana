using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What the inspector needs of one URL to follow its redirects and judge where they end, as the analysis's
/// <see cref="Analysis.Models.UrlStatus"/> holds it for every URL.
/// </summary>
/// <param name="HasResponse">The crawl stored a response, a "not checked" one included.</param>
/// <param name="NotChecked">Stored without a request, its host having stopped answering: as good as never fetched.</param>
/// <param name="IsParsed">Its HTML was read, and it's a page the analysis audits (not one of Cloudflare's /cdn-cgi/ ones).</param>
/// <param name="RedirectTargetId">Where an HTTP redirect points, or 0.</param>
internal sealed record InspectedUrl(
    int Id,
    string Url,
    bool IsInternal,
    bool HasResponse,
    bool NotChecked,
    FetchOutcome? Outcome,
    int? StatusCode,
    bool IsHtml,
    bool IsParsed,
    int RedirectTargetId)
{
    /// <summary>Statuses many sites send crawlers but not people, which don't make another site's URL broken.</summary>
    private static readonly HashSet<int> BOT_REFUSALS = [401, 403, 405, 429, 503, 999];

    public bool Fetched => HasResponse && !NotChecked;

    /// <summary>The status as the grids show it: none when not fetched, or no HTTP answer.</summary>
    public int? Status => Fetched && StatusCode > 0 ? StatusCode : null;

    public UrlOutcome? Shown => !HasResponse ? null : NotChecked ? UrlOutcome.NotChecked : (UrlOutcome?)Outcome;

    /// <summary>An HTML page the crawler fetched and parsed, as the analysis's <c>UrlStatus.IsPage</c>.</summary>
    public bool IsPage => Fetched && Outcome == FetchOutcome.Success && IsHtml && IsParsed;

    /// <summary>Checked and not working, as the analysis's <c>UrlStatus.IsBroken</c> judges it.</summary>
    public bool IsBroken => Fetched && (Outcome is FetchOutcome.Timeout or FetchOutcome.NetworkError ||
                                        (Outcome is FetchOutcome.ClientError or FetchOutcome.ServerError && (IsInternal || !BOT_REFUSALS.Contains(StatusCode ?? 0))));
}
