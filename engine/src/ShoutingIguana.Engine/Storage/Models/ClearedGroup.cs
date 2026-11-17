namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A cause of a check in the other crawl with no URL now, and how its URLs then moved.</summary>
/// <param name="Detail">Its detail or fault then; null for a check grouped by the other URL.</param>
/// <param name="RelatedUrl">Its related URL then, as text: for a duplicate cluster, its first page then.</param>
/// <param name="SharedText">What a duplicate cluster's pages shared then (the title, the description or the H1); null for any other cause.</param>
/// <param name="Then">Its URLs then.</param>
/// <param name="Fixed">Of those, the ones this crawl judged and doesn't flag.</param>
/// <param name="Gone">Of those, the ones this crawl didn't find.</param>
/// <param name="NotChecked">Of those, the ones not flagged now that this crawl didn't judge. The rest are still flagged, for another cause.</param>
public sealed record ClearedGroup(string? Detail, string? RelatedUrl, string? SharedText, long Then, long Fixed, long Gone, long NotChecked);
