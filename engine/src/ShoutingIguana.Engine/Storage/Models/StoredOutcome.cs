using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a URL's stored response was, as the writer needs it to fetch the URL again and keep the live counts right.</summary>
/// <param name="IsPage">A parsed HTML page, as <see cref="CrawlSchema.PAGE"/> has it but for being internal, which the caller knows.</param>
/// <param name="IsNotChecked">A network error stored without a request, because the URL's host had stopped answering.</param>
/// <param name="Bytes">The body the crawler read, or 0 when it only checked the URL.</param>
internal readonly record struct StoredOutcome(FetchOutcome Outcome, int? StatusCode, bool IsPage, bool IsNotChecked, long Bytes);
