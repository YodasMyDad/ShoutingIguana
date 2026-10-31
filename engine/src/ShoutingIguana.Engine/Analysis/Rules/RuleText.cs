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
        FetchOutcome.Redirect => $"redirects (HTTP {url.StatusCode})",
        FetchOutcome.Timeout => "timed out",
        FetchOutcome.NetworkError => "couldn't be reached",
        FetchOutcome.Blocked => "is blocked by a firewall",
        FetchOutcome.RobotsDisallowed => "is blocked by robots.txt",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Noindex => "is noindex",
        FetchOutcome.Success when url.IndexabilityReason == IndexabilityReason.Canonicalised => "is canonicalised to another URL",
        _ => $"returns HTTP {url.StatusCode}"
    };

    public static string Size(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d:0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB");
}
