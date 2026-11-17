using System.Text.Json.Nodes;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One way two crawls compared differ (<see cref="Analysis.CrawlComparability"/>), so the UI can say it in its own words.</summary>
/// <param name="Field">
/// The option or the analysis's property that differs, as the API names it (<c>maxUrls</c>, <c>analysisVersion</c>); null
/// for a difference that is the whole of its code.
/// </param>
/// <param name="Side">The crawl it is about, for <see cref="ComparisonDifferenceCode.StoppedEarly"/>; null otherwise.</param>
/// <param name="Then">The value in the other crawl: a number, a boolean, a text or a list of texts; null for none (no limit) or a value not stored.</param>
/// <param name="Now">The value in this crawl, as <paramref name="Then"/>.</param>
public sealed record ComparisonDifference(
    ComparisonDifferenceCode Code,
    string? Field = null,
    ComparisonSide? Side = null,
    JsonNode? Then = null,
    JsonNode? Now = null);
