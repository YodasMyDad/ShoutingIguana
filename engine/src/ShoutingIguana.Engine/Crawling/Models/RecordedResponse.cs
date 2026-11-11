using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>What the live counts need of one stored response, as it is recorded or dropped.</summary>
/// <param name="IsSitePage">A page, as <c>CrawlSchema.PAGE</c> defines one: internal, HTML, fetched and parsed.</param>
/// <param name="IsSiteUrl">One of the site's own URLs: internal, and not a probe of the site's other versions.</param>
/// <param name="IsNotChecked">A network error stored without a request, because the URL's host had stopped answering.</param>
/// <param name="Bytes">The body the crawler read; a size it was only told isn't counted.</param>
/// <param name="IsProbe">The answer of a probe of the site's other versions, on the site or where one redirects.</param>
internal readonly record struct RecordedResponse(FetchOutcome Outcome, int? StatusCode, bool IsSitePage, bool IsSiteUrl, bool IsNotChecked, long Bytes,
    bool IsProbe = false);
