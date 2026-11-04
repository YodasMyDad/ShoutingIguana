namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One test of a URL's indexability, and what the crawl shows of it.</summary>
/// <param name="StatusCode">For <see cref="IndexabilityTestKind.Answered"/>: the status, when there was one.</param>
/// <param name="MetaRefresh">For <see cref="IndexabilityTestKind.Answered"/>: where an instant meta refresh moves on to.</param>
/// <param name="Unreachable">For <see cref="IndexabilityTestKind.RobotsTxt"/>: robots.txt couldn't be read, which refuses everything.</param>
/// <param name="Meta">For <see cref="IndexabilityTestKind.Noindex"/>: a robots meta tag says noindex; null when the HTML wasn't read.</param>
/// <param name="Header">For <see cref="IndexabilityTestKind.Noindex"/>: the X-Robots-Tag header says noindex.</param>
/// <param name="Canonical">For <see cref="IndexabilityTestKind.Canonical"/>: the canonical a search engine honours, when the crawl has the URL.</param>
/// <param name="CanonicalUrl">
/// For <see cref="IndexabilityTestKind.Canonical"/>: that canonical as the page gives it, normalised, whether the crawl has the
/// URL or not (a page only status-checked keeps no links, so its Link header's canonical is no URL of the crawl's).
/// </param>
public sealed record IndexabilityTest(IndexabilityTestKind Test, TestOutcome Outcome)
{
    public int? StatusCode { get; init; }

    public UrlTarget? MetaRefresh { get; init; }

    public bool? Unreachable { get; init; }

    public bool? Meta { get; init; }

    public bool? Header { get; init; }

    public UrlTarget? Canonical { get; init; }

    public string? CanonicalUrl { get; init; }
}
