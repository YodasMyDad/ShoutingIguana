namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl and the crawl it is compared with (the fix loop).</summary>
/// <param name="Other">The other crawl; null when the crawl has no previous one to compare with.</param>
/// <param name="Picked">The other crawl was named, not chosen as the previous one.</param>
internal sealed record ComparedPair(LibraryEntry Current, LibraryEntry? Other, bool Picked);
