using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// What the site's redirects say in their headers besides where they go: a canonical that names another URL than the
/// one they end at, and a temporary redirect browsers may keep for more than a day. A redirect has no page facts, so its
/// stored headers are read once every page is known.
/// </summary>
/// <param name="readRedirectHeaders">The headers of each of the site's own URLs that redirected (<c>AnalysisReader.ReadRedirectHeaders</c>).</param>
/// <param name="readUrl">The stored URL with an id, or null.</param>
internal sealed class RedirectHeaderRules(
    UrlTable urls,
    Func<IEnumerable<(int UrlId, IReadOnlyList<ResponseHeader> Headers)>> readRedirectHeaders,
    Func<int, string?> readUrl) : ISiteRule
{
    /// <summary>A temporary redirect browsers may keep longer than this, in seconds, is cached for long: a day.</summary>
    private const long CACHED_FOR_LONG = 86_400;

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.CANONICAL_ON_REDIRECT, IssueCatalog.REDIRECT_TEMPORARY_CACHED];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var (id, headers) in readRedirectHeaders())
        {
            if (CanonicalOnRedirect(id, headers) is { } hit)
            {
                yield return hit;
            }

            if (TemporaryCachedForLong(id, headers) is { } cached)
            {
                yield return cached;
            }
        }
    }

    /// <summary>
    /// A temporary redirect within the site (as the temporary redirect check judges it) that browsers and proxies may keep
    /// for more than a day: a <c>Cache-Control</c> max-age or s-maxage over a day, or an <c>Expires</c> more than a day after
    /// its <c>Date</c>, with no no-store or no-cache.
    /// </summary>
    private IssueHit? TemporaryCachedForLong(int id, IReadOnlyList<ResponseHeader> headers)
    {
        var url = urls[id];
        if (url.StatusCode is not (302 or 303 or 307) || url.RedirectTargetId == 0 || !urls[url.RedirectTargetId].IsInternal)
        {
            return null;
        }

        var cacheControl = string.Join(',', headers.Where(header => header.Name == "cache-control").Select(header => header.Value));
        var directives = cacheControl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split('=', 2) is [var name, var value] ? (Name: name.Trim().ToLowerInvariant(), Value: value.Trim().Trim('"')) : (Name: directive.ToLowerInvariant(), Value: string.Empty))
            .ToList();
        if (directives.Any(directive => directive.Name is "no-store" or "no-cache"))
        {
            return null;
        }

        var maxAge = directives.Where(directive => directive.Name is "max-age" or "s-maxage")
            .Select(directive => long.TryParse(directive.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : -1)
            .DefaultIfEmpty(-1).Max();
        var lifetime = maxAge >= 0 ? maxAge : ExpiresAfter(headers);
        return lifetime > CACHED_FOR_LONG
            ? new IssueHit(IssueCatalog.REDIRECT_TEMPORARY_CACHED, id, $"{HttpStatusNames.Describe(url.StatusCode)}, cached for {Lifetime(lifetime)}")
            : null;
    }

    /// <summary>How long after its Date an Expires header lets a response be kept, in seconds; -1 without both.</summary>
    private static long ExpiresAfter(IReadOnlyList<ResponseHeader> headers)
    {
        static DateTimeOffset? Read(IReadOnlyList<ResponseHeader> headers, string name) =>
            headers.FirstOrDefault(header => header.Name == name).Value is { } value &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? date
                : null;

        return Read(headers, "expires") is { } expires && Read(headers, "date") is { } date ? (long)(expires - date).TotalSeconds : -1;
    }

    /// <summary>"30 days", or under two days, "36 hours".</summary>
    private static string Lifetime(long seconds) => seconds >= 2 * CACHED_FOR_LONG
        ? RuleText.Count((int)Math.Min(seconds / CACHED_FOR_LONG, int.MaxValue), "day", "days")
        : RuleText.Count((int)(seconds / 3_600), "hour", "hours");

    /// <summary>
    /// A redirect whose <c>Link</c> header names a canonical other than where its redirects end: search engines follow
    /// the redirect, so the header only misleads. One whose canonical is where it ends agrees with it, and one that ends
    /// nowhere (no usable Location) has nothing to compare with.
    /// </summary>
    private IssueHit? CanonicalOnRedirect(int id, IReadOnlyList<ResponseHeader> headers)
    {
        var links = headers.Where(header => header.Name == "link").Select(header => header.Value).ToList();
        if (links.Count == 0 || readUrl(id) is not { } url)
        {
            return null;
        }

        var canonical = LinkHeaderParser.Parse(links).FirstOrDefault(entry => entry.Rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical"));
        var finalId = urls.TraceRedirects(id).FinalId;
        if (canonical is null || finalId == id || readUrl(finalId) is not { } final ||
            !UrlNormalizer.TryResolve(new Uri(url), canonical.Url, out var named))
        {
            return null;
        }

        var namedUrl = UrlNormalizer.Normalize(named);
        return namedUrl == final
            ? null
            : new IssueHit(IssueCatalog.CANONICAL_ON_REDIRECT, id, RuleText.Fit($"Redirects to {final}; its canonical header names {namedUrl}"));
    }
}
