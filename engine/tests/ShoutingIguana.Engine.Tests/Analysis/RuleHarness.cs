using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Builds analysed pages and URL statuses for rule tests without a crawl. URLs get ids as they're first mentioned;
/// a URL nobody sets a status for stays known but unfetched, as a skipped URL would be.
/// </summary>
internal sealed class RuleHarness(string site = "https://example.com")
{
    private readonly List<UrlStatus> _statuses = [default];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    // Internal as a crawl from the site's home page sees it: the www twin is the same site.
    private readonly CrawlScope _scope = new(new Uri(site), includeSubdomains: false, stayInStartFolder: false);

    public string Site { get; } = site;

    public int Id(string url)
    {
        var normalized = UrlNormalizer.Normalize(new Uri(url));
        if (_ids.TryGetValue(normalized, out var id))
        {
            return id;
        }

        var uri = new Uri(normalized);
        _statuses.Add(new UrlStatus(Known: true, IsInternal: _scope.IsInternal(uri), IsHttps: uri.Scheme == Uri.UriSchemeHttps, Depth: 1,
            Fetched: false, default, 0, IsHtml: false, IsParsed: false, IndexabilityReason.None, 0, null, InSitemap: false,
            IsResource: ResourceTypes.IsResource(normalized, null)));
        _ids[normalized] = _statuses.Count - 1;
        return _statuses.Count - 1;
    }

    /// <summary>Records what the crawl found at a URL.</summary>
    public int Fetched(string url, FetchOutcome outcome, int status, bool isHtml = false, IndexabilityReason reason = IndexabilityReason.None,
        string? redirectTo = null, long? size = null, bool inSitemap = false, string? contentType = null)
    {
        var id = Id(url);
        contentType ??= isHtml ? "text/html; charset=utf-8" : null;
        _statuses[id] = _statuses[id] with
        {
            IsResource = ResourceTypes.IsResource(UrlNormalizer.Normalize(new Uri(url)), outcome == FetchOutcome.Success ? contentType : null),
            Fetched = true,
            Outcome = outcome,
            StatusCode = status,
            IsHtml = isHtml,
            IsParsed = isHtml,
            IndexabilityReason = reason,
            RedirectTargetId = redirectTo is null ? 0 : Id(redirectTo),
            SizeBytes = size,
            InSitemap = inSitemap
        };
        return id;
    }

    public int Ok(string url, IndexabilityReason reason = IndexabilityReason.None, bool inSitemap = false) =>
        Fetched(url, FetchOutcome.Success, 200, isHtml: true, reason, inSitemap: inSitemap);

    /// <summary>Parses HTML the way the crawler does and records the page as a successful HTML response.</summary>
    public AnalyzedPage Page(string url, string html, IndexabilityReason reason = IndexabilityReason.None,
        IReadOnlyList<ResponseHeader>? headers = null, int responseMs = 100, long? bodyBytes = null)
    {
        var uri = new Uri(UrlNormalizer.Normalize(new Uri(url)));
        var parsed = HtmlPageParser.Parse(html, uri, headers ?? [], "ShoutingIguana");
        var id = Ok(uri.AbsoluteUri, reason);
        var links = parsed.Links
            .Select(link => new StoredLink(Id(link.Target.AbsoluteUri), link.Kind, link.Flags, link.Position, link.Text, UrlNormalizer.GetFragment(link.Target)))
            .ToList();
        return new AnalyzedPage
        {
            UrlId = id,
            Url = UrlNormalizer.Normalize(uri),
            Uri = uri,
            IndexabilityReason = reason,
            Headers = headers ?? [],
            Facts = parsed.Facts,
            Links = links,
            ResponseMs = responseMs,
            BodyBytes = bodyBytes ?? html.Length
        };
    }

    /// <param name="instantRefreshes">Pages with a 0-second meta refresh and where it goes, as the reader finds them.</param>
    /// <param name="canonicals">Canonicalised pages and their canonical's target, as the reader finds them.</param>
    public UrlTable Table(string? start = null, IEnumerable<(int Page, int Target)>? instantRefreshes = null,
        IEnumerable<(int Page, int Target)>? canonicals = null)
    {
        // Register the start URL before taking the snapshot.
        var startId = Id(start ?? Site + "/");
        return new UrlTable([.. _statuses], startId, instantRefreshes, canonicals);
    }

    /// <summary>The page with the indexability the table settled on, as the reader hands pages to the rules.</summary>
    public static AnalyzedPage AsAnalysed(AnalyzedPage page, UrlTable urls) => page with { IndexabilityReason = urls[page.UrlId].IndexabilityReason };

    /// <summary>A document whose head and body are clean apart from what the test passes in.</summary>
    public static string Html(string head = "", string body = "", string lang = " lang=\"en\"") =>
        $"<!DOCTYPE html><html{lang}><head><meta charset=\"utf-8\">{head}</head><body><main>{body}</main></body></html>";
}
