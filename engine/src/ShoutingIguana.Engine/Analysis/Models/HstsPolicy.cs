using System.Globalization;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// The Strict-Transport-Security a site's home page sends over HTTPS: a browser that has seen it asks the home page's
/// host (and, with includeSubDomains, its subdomains) only over HTTPS until it lapses, so it never loads an HTTP copy of
/// a page there (RFC 6797).
/// </summary>
/// <param name="Host">The home page's host.</param>
internal sealed record HstsPolicy(string Host, bool IncludeSubDomains)
{
    /// <summary>
    /// The policy the home page's headers set, or null: none sent, sent over plain HTTP (where browsers ignore it), or
    /// with a max-age missing, unreadable or 0, which sets no policy or ends one.
    /// </summary>
    public static HstsPolicy? Of(Uri home, IReadOnlyList<ResponseHeader> headers)
    {
        if (home.Scheme != Uri.UriSchemeHttps || headers.FirstOrDefault(header => header.Name == "strict-transport-security").Value is not { } header ||
            MaxAge(header) is not (_, > 0))
        {
            return null;
        }

        var includeSubDomains = Directives(header).Any(directive => directive.Equals("includeSubDomains", StringComparison.OrdinalIgnoreCase));
        return new HstsPolicy(home.Host, includeSubDomains);
    }

    /// <summary>
    /// A Strict-Transport-Security header's max-age: its value as written (unquoted), or null when there's no max-age, and
    /// its seconds when they read as a whole number.
    /// </summary>
    public static (string? Written, long? Seconds)? MaxAge(string header)
    {
        if (Directives(header).FirstOrDefault(directive => directive.StartsWith("max-age", StringComparison.OrdinalIgnoreCase)) is not { } maxAge)
        {
            return null;
        }

        var written = maxAge.Split('=', 2) is [_, var value] ? value.Trim().Trim('"') : null;
        return (written, long.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : null);
    }

    /// <summary>Whether a browser that has seen the policy asks this URL only over HTTPS.</summary>
    public bool Covers(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps &&
        (url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || IncludeSubDomains && url.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Directives(string header) => header.Split(';').Select(part => part.Trim());
}
