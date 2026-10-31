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

    /// <summary>What the crawl found at a URL, phrased to follow "the URL": "returns HTTP 404", "is noindex".</summary>
    public static string Status(UrlStatus url) => url.Outcome switch
    {
        FetchOutcome.Redirect when url.IsMetaRefresh => "redirects with an instant meta refresh",
        FetchOutcome.Redirect when url.RedirectTargetId == 0 => $"redirects (HTTP {url.StatusCode}) without a usable Location header",
        FetchOutcome.Redirect => $"redirects (HTTP {url.StatusCode})",
        FetchOutcome.Timeout => "timed out",
        FetchOutcome.NetworkError => "couldn't be reached",
        FetchOutcome.Blocked => "is blocked by a firewall",
        FetchOutcome.RobotsDisallowed => "is blocked by robots.txt",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Noindex => "is noindex",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Canonicalised => "is canonicalised to another URL",
        _ => $"returns HTTP {url.StatusCode}"
    };

    /// <summary>
    /// What's wrong with a URL that <see cref="UrlTable.IsBrokenThroughRedirects"/>, phrased to follow "the URL":
    /// "returns HTTP 404", or for a redirect where it goes wrong, "redirects to a URL that returns HTTP 404".
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

    public static string Size(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d:0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB");
}
