namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A page of a crawl's extracted values.</summary>
/// <param name="Rule">Only this rule's rows, or null for every rule.</param>
/// <param name="Search">Matched against the page's URL and the extracted text, or null.</param>
/// <param name="Offset">Rows to skip. Negative is none.</param>
/// <param name="Limit">Rows to return, at most <see cref="ExtractionReader.MAX_LIMIT"/>.</param>
public sealed record ExtractionQuery(string? Rule, string? Search, int Offset, int Limit);
