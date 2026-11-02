using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>Why the URLs with no status have none. Together they make up <c>statusClasses.noResponse</c>.</summary>
/// <param name="RobotsDisallowed">robots.txt refused them (by a rule, or because it couldn't be read): never requested.</param>
/// <param name="NetworkError">DNS, a refused or reset connection, TLS: not counting <paramref name="NotChecked"/>.</param>
/// <param name="NotChecked">Never asked, their host having stopped answering: unknown, not broken.</param>
/// <param name="ProcessingError">An answer came, but the crawler failed to process it.</param>
internal sealed record NoResponseDto(long RobotsDisallowed, long Timeout, long NetworkError, long NotChecked, long ProcessingError)
{
    public static NoResponseDto From(CrawlNoResponseCounts counts) =>
        new(counts.RobotsDisallowed, counts.Timeout, counts.NetworkError, counts.NotChecked, counts.ProcessingError);
}
