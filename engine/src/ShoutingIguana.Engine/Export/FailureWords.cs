using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Export.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// How a request failed, in the plain words the app's screens say it in (the app's <c>lib/failure.ts</c>): a status by
/// its name, else what the failure comes down to ("it timed out", "the DNS name wasn’t found"). An export of a list
/// writes a sitemap's or an origin's state as its screen's cell shows it, so the two read the same.
/// </summary>
internal static partial class FailureWords
{
    /// <summary>The kinds the engine's text is read for, in the order they're tried: a name that isn't found is never "refused".</summary>
    private static readonly (Regex Pattern, string Words)[] KINDS =
    [
        (Dns(), "the DNS name wasn’t found"),
        (Tls(), "a secure connection couldn’t be made"),
        (Refused(), "the connection was refused"),
        (TimedOut(), "it timed out")
    ];

    /// <summary>A failure as the crawl recorded it, in plain words; null when it recorded nothing.</summary>
    public static PlainFailure? Of(string? text)
    {
        var said = text?.Trim() ?? string.Empty;
        if (said.Length == 0)
        {
            return null;
        }

        // An earlier engine's "HTTP 503" is named as the engine names a status now; "503 Service Unavailable" (and
        // "HTTP 520", a status with no name) is said as it is.
        if (BareStatus().Match(said) is { Success: true } bare)
        {
            return new PlainFailure(HttpStatusNames.Describe(int.Parse(bare.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)), IsOther: false);
        }

        if (NamedStatus().IsMatch(said))
        {
            return new PlainFailure(said, IsOther: false);
        }

        foreach (var (pattern, words) in KINDS)
        {
            if (pattern.IsMatch(said))
            {
                return new PlainFailure(words, IsOther: false);
            }
        }

        return new PlainFailure("the request failed", IsOther: true);
    }

    [GeneratedRegex(@"^HTTP (\d{3})$", RegexOptions.IgnoreCase)]
    private static partial Regex BareStatus();

    [GeneratedRegex(@"^\d{3} [A-Za-z][A-Za-z '-]*$")]
    private static partial Regex NamedStatus();

    [GeneratedRegex("DNS lookup failed|name resolution|no such host|name or service not known|nodename nor servname", RegexOptions.IgnoreCase)]
    private static partial Regex Dns();

    [GeneratedRegex(@"\bTLS\b|\bSSL\b|certificate|secure channel|handshake", RegexOptions.IgnoreCase)]
    private static partial Regex Tls();

    [GeneratedRegex("actively refused|connection refused|refused the connection", RegexOptions.IgnoreCase)]
    private static partial Regex Refused();

    [GeneratedRegex("timed out|time-?out|no response within|did not properly respond", RegexOptions.IgnoreCase)]
    private static partial Regex TimedOut();
}
