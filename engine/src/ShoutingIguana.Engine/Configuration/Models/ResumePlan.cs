namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>How a stored crawl would resume, from <see cref="CrawlOptions.PlanResume"/>.</summary>
/// <param name="Options">The stored options with the changes applied. Validate them before resuming.</param>
/// <param name="MissingHeaders">Stored header names that weren't given again, so they won't be sent.</param>
/// <param name="MissingProxyCredentials">
/// The stored proxy had a user name and password, which crawl files don't keep, and no proxy was given again. The
/// proxy in <paramref name="Options"/> can't be used as it is.
/// </param>
public sealed record ResumePlan(CrawlOptions Options, IReadOnlyList<string> MissingHeaders, bool MissingProxyCredentials);
