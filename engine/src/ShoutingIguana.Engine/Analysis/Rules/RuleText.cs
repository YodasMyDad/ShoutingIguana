using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Shared wording and measuring for issue details.</summary>
internal static class RuleText
{
    /// <summary>Length as a reader counts it: user-perceived characters, so an emoji or accented letter counts once.</summary>
    public static int Length(string text) => new StringInfo(text.Trim()).LengthInTextElements;

    public static string Count(int count, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? singular : plural)}");

    public static string Characters(int count) => Count(count, "character", "characters");

    /// <summary>"45 characters", or for text with wide characters "21 characters (42 counting full-width characters as two)".</summary>
    public static string Width((int Characters, int Width) measured) => measured.Width == measured.Characters
        ? Characters(measured.Characters)
        : string.Create(CultureInfo.InvariantCulture, $"{Characters(measured.Characters)} ({measured.Width:N0} counting full-width characters as two)");

    /// <summary>What the crawl found at a URL, phrased to follow "the URL": "returns 404 Not Found", "is noindex".</summary>
    public static string Status(UrlStatus url) => url.Outcome switch
    {
        FetchOutcome.Redirect when url.IsMetaRefresh => "redirects with an instant meta refresh",
        FetchOutcome.Redirect when url.RedirectTargetId == 0 => $"redirects with {HttpStatusNames.Describe(url.StatusCode)} but no usable Location header",
        FetchOutcome.Redirect => $"redirects ({HttpStatusNames.Describe(url.StatusCode)})",
        FetchOutcome.Timeout => "timed out",
        FetchOutcome.NetworkError => "couldn't be reached",
        FetchOutcome.Blocked => "is blocked by a firewall",
        FetchOutcome.RobotsDisallowed => "is blocked by robots.txt",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Noindex => "is noindex",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Canonicalised => "is canonicalised to another URL",
        _ => $"returns {HttpStatusNames.Describe(url.StatusCode)}"
    };

    /// <summary>
    /// What's wrong with a URL that <see cref="UrlTable.IsBrokenThroughRedirects"/>, phrased to follow "the URL":
    /// "returns 404 Not Found", or for a redirect where it goes wrong, "redirects to a URL that returns 404 Not Found".
    /// </summary>
    public static string BrokenStatus(UrlTable urls, int id)
    {
        var trace = urls.TraceRedirects(id);
        var final = urls[trace.FinalId];
        return trace switch
        {
            { Hops: 0 } => Status(final),
            { IsLoop: true } => "redirects in a loop",
            _ when final is { Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 } =>
                $"is still redirecting after {Count(trace.Hops, "hop", "hops")}, more than browsers follow",
            _ => $"redirects to a URL that {Status(final)}"
        };
    }

    /// <summary>
    /// "2 broken URLs", saying where the first one goes wrong when that's only at the end of its redirects, since the
    /// first is the issue's related URL and a redirect there would look fine: "1 image (it redirects to a URL that returns 404 Not Found)".
    /// </summary>
    public static string BrokenTargets(UrlTable urls, TargetSet targets, string singular, string plural)
    {
        var count = Count(targets.Count, singular, plural);
        return targets.First is { } first && urls[first].Outcome == FetchOutcome.Redirect
            ? $"{count} ({(targets.Count == 1 ? "it" : "the first")} {BrokenStatus(urls, first)})"
            : count;
    }

    public static string Size(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d:0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB");

    /// <summary>A limit's size as a setting says it: "1 MB", "200 KB", "1.5 MB".</summary>
    public static string LimitSize(long bytes) => Size(bytes).Replace(".0 MB", " MB", StringComparison.Ordinal);

    /// <summary>The most characters a detail takes: one line beside a URL.</summary>
    public const int DETAIL_MAX = 120;

    /// <summary>The most characters one quoted text takes in a detail that quotes several.</summary>
    public const int QUOTE_MAX = 40;

    /// <summary>Text as a reader counts it, cut to <paramref name="max"/> characters with "…" where it's longer.</summary>
    public static string Cut(string text, int max)
    {
        var info = new StringInfo(text);
        return info.LengthInTextElements <= max ? text : info.SubstringByTextElements(0, Math.Max(max - 1, 0)) + "…";
    }

    /// <summary>Text from the page in quotes, its runs of white space as one space, cut to <paramref name="max"/> characters: "Sofas | Example".</summary>
    public static string Quote(string text, int max = QUOTE_MAX) =>
        $"\"{Cut(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), max)}\"";

    /// <summary>A detail cut to <see cref="DETAIL_MAX"/> characters.</summary>
    public static string Fit(string detail) => Cut(detail, DETAIL_MAX);

    /// <summary>A count, then what it counts, quoted: "2 titles: "A" · "B"".</summary>
    public static string Listed(int count, string singular, string plural, IEnumerable<string> texts) =>
        Fit($"{Count(count, singular, plural)}: {string.Join(" · ", texts.Select(text => Quote(text)))}");

    /// <summary>
    /// What ended a page's head early, for a link the head should hold: "&lt;img src="/t.gif"&gt; on line 12 ended the head
    /// early", "It's in the body (after &lt;body&gt; on line 30)". Null for a page crawled before the crawler noted it.
    /// </summary>
    public static string? HeadEnded(HeadEndFact? headEnd) => headEnd switch
    {
        null => null,
        { Implicit: false } => headEnd.Line is { } line
            ? string.Create(CultureInfo.InvariantCulture, $"It's in the body (after <body> on line {line:N0})")
            : "It's in the body",
        { Line: { } at } => Fit(string.Create(CultureInfo.InvariantCulture, $"{headEnd.Element} on line {at:N0} ended the head early")),
        _ => Fit($"{char.ToUpperInvariant(headEnd.Element[0])}{headEnd.Element[1..]} ended the head early")
    };

    /// <summary>A measurement over its limit: "78 characters, over 60".</summary>
    public static string Over(string measured, long limit) => string.Create(CultureInfo.InvariantCulture, $"{measured}, over {limit:N0}");

    /// <summary>A measurement under its limit: "12 characters, under 30".</summary>
    public static string Under(string measured, long limit) => string.Create(CultureInfo.InvariantCulture, $"{measured}, under {limit:N0}");

    /// <summary>
    /// A URL as a detail shows it beside the page it's about: its path and query when it's on the page's own scheme and
    /// host, the whole URL otherwise.
    /// </summary>
    public static string Near(string url, string pageUrl) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) &&
        uri.Scheme == page.Scheme && string.Equals(uri.Authority, page.Authority, StringComparison.OrdinalIgnoreCase)
            ? uri.PathAndQuery
            : url;
}
