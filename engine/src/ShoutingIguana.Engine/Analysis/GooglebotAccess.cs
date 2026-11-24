using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Whether Googlebot may read a URL the crawl's own robots.txt group refused. A site can turn every crawler away but the
/// search engines it names (<c>User-agent: *</c> with <c>Disallow: /</c>, then <c>User-agent: Googlebot</c> with
/// <c>Allow: /</c>): its pages are refused to this crawl and open to Google, so they aren't out of search.
/// </summary>
/// <param name="robots">Each origin's robots.txt as the crawl read it.</param>
/// <param name="readUrl">The stored URL with an id.</param>
internal sealed class GooglebotAccess(IReadOnlyList<RobotsRecord> robots, Func<int, string?> readUrl)
{
    private const string GOOGLEBOT = "Googlebot";

    /// <summary>Each origin's robots.txt as Googlebot reads it; null for an origin whose file the crawl couldn't read.</summary>
    private readonly Dictionary<string, RobotsTxt?> _parsed = new(StringComparer.Ordinal);

    /// <summary>True when the origin's robots.txt was read and its rules for Googlebot allow the URL.</summary>
    public bool Allows(int id)
    {
        if (readUrl(id) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!_parsed.TryGetValue(origin, out var parsed))
        {
            _parsed[origin] = parsed = robots.FirstOrDefault(file => file.Origin == origin) is { Content: { } content }
                ? RobotsTxtParser.Parse(content, GOOGLEBOT)
                : null;
        }

        return parsed?.IsAllowed(uri.PathAndQuery) == true;
    }
}
