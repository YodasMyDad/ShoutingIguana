using ShoutingIguana.Engine.Analysis.Rules;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>A status code in an issue's detail carries its standard name; one with no standard name stays as it was.</summary>
public sealed class HttpStatusNamesTests
{
    [Theory]
    [InlineData(200, "200 OK")]
    [InlineData(301, "301 Moved Permanently")]
    [InlineData(302, "302 Found")]
    [InlineData(307, "307 Temporary Redirect")]
    [InlineData(308, "308 Permanent Redirect")]
    [InlineData(403, "403 Forbidden")]
    [InlineData(404, "404 Not Found")]
    [InlineData(410, "410 Gone")]
    [InlineData(429, "429 Too Many Requests")]
    [InlineData(500, "500 Internal Server Error")]
    [InlineData(502, "502 Bad Gateway")]
    [InlineData(503, "503 Service Unavailable")]
    [InlineData(504, "504 Gateway Timeout")]
    public void AStandardCodeCarriesItsName(int statusCode, string described) => Assert.Equal(described, HttpStatusNames.Describe(statusCode));

    /// <summary>A CDN's own codes, LinkedIn's 999, the unused 418 and anything out of range have no standard name.</summary>
    [Theory]
    [InlineData(599)]
    [InlineData(520)]
    [InlineData(999)]
    [InlineData(418)]
    [InlineData(299)]
    [InlineData(0)]
    public void ACodeWithNoStandardNameStaysAsItWas(int statusCode) => Assert.Equal($"HTTP {statusCode}", HttpStatusNames.Describe(statusCode));

    /// <summary>What the crawl stored with a status in it reads the same way once a detail quotes it; the rest of the text is left alone.</summary>
    [Theory]
    [InlineData("Rate limited (HTTP 429)", "Rate limited (429 Too Many Requests)")]
    [InlineData("HTTP 500", "500 Internal Server Error")]
    [InlineData("robots.txt unreachable: HTTP 503", "robots.txt unreachable: 503 Service Unavailable")]
    [InlineData("HTTP 301 redirects to no http or https URL", "301 Moved Permanently redirects to no http or https URL")]
    [InlineData("HTTP 599", "HTTP 599")]
    [InlineData("HTTP 404, then HTTP 410", "404 Not Found, then 410 Gone")]
    [InlineData("Timed out", "Timed out")]
    [InlineData("2 HTTP resources", "2 HTTP resources")]
    [InlineData("HTTP 4040", "HTTP 4040")]
    [InlineData("XHTTP 404", "XHTTP 404")]
    [InlineData("HTTP ٤٠٤", "HTTP ٤٠٤")]
    public void AStatusInStoredTextIsNamed(string stored, string named) => Assert.Equal(named, HttpStatusNames.NameIn(stored));
}
