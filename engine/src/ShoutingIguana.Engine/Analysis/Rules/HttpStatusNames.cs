using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// A status code with its standard name, as an issue's detail gives it: "404 Not Found". The names are the reason phrases
/// of RFC 9110 and the IANA HTTP status code registry. A code with no standard name (a CDN's 520, LinkedIn's 999, the
/// unused 418) stays "HTTP 520": its number is all there is to say.
/// </summary>
internal static partial class HttpStatusNames
{
    private static readonly Dictionary<int, string> NAMES = new()
    {
        [100] = "Continue",
        [101] = "Switching Protocols",
        [102] = "Processing",
        [103] = "Early Hints",
        [200] = "OK",
        [201] = "Created",
        [202] = "Accepted",
        [203] = "Non-Authoritative Information",
        [204] = "No Content",
        [205] = "Reset Content",
        [206] = "Partial Content",
        [207] = "Multi-Status",
        [208] = "Already Reported",
        [226] = "IM Used",
        [300] = "Multiple Choices",
        [301] = "Moved Permanently",
        [302] = "Found",
        [303] = "See Other",
        [304] = "Not Modified",
        [305] = "Use Proxy",
        [307] = "Temporary Redirect",
        [308] = "Permanent Redirect",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [402] = "Payment Required",
        [403] = "Forbidden",
        [404] = "Not Found",
        [405] = "Method Not Allowed",
        [406] = "Not Acceptable",
        [407] = "Proxy Authentication Required",
        [408] = "Request Timeout",
        [409] = "Conflict",
        [410] = "Gone",
        [411] = "Length Required",
        [412] = "Precondition Failed",
        [413] = "Content Too Large",
        [414] = "URI Too Long",
        [415] = "Unsupported Media Type",
        [416] = "Range Not Satisfiable",
        [417] = "Expectation Failed",
        [421] = "Misdirected Request",
        [422] = "Unprocessable Content",
        [423] = "Locked",
        [424] = "Failed Dependency",
        [425] = "Too Early",
        [426] = "Upgrade Required",
        [428] = "Precondition Required",
        [429] = "Too Many Requests",
        [431] = "Request Header Fields Too Large",
        [451] = "Unavailable For Legal Reasons",
        [500] = "Internal Server Error",
        [501] = "Not Implemented",
        [502] = "Bad Gateway",
        [503] = "Service Unavailable",
        [504] = "Gateway Timeout",
        [505] = "HTTP Version Not Supported",
        [506] = "Variant Also Negotiates",
        [507] = "Insufficient Storage",
        [508] = "Loop Detected",
        [510] = "Not Extended",
        [511] = "Network Authentication Required"
    };

    /// <summary>"404 Not Found", or "HTTP 599" for a code with no standard name.</summary>
    public static string Describe(int statusCode) => NAMES.TryGetValue(statusCode, out var name)
        ? string.Create(CultureInfo.InvariantCulture, $"{statusCode} {name}")
        : string.Create(CultureInfo.InvariantCulture, $"HTTP {statusCode}");

    /// <summary>
    /// Text the crawl stored with a status in it, as a detail quotes it: a firewall's "Rate limited (HTTP 429)" reads
    /// "Rate limited (429 Too Many Requests)", a sitemap's "HTTP 500" reads "500 Internal Server Error". The crawl file
    /// keeps its own words, so a crawl made by any engine reads the same once analysed.
    /// </summary>
    public static string NameIn(string text) =>
        StatusInText().Replace(text, match => Describe(int.Parse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture)));

    [GeneratedRegex(@"\bHTTP ([0-9]{3})\b", RegexOptions.CultureInvariant)]
    private static partial Regex StatusInText();
}
