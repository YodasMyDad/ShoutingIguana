namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary><c>library.json</c>: what the library adds to and takes from the data folder's crawls.</summary>
/// <param name="Opened">Crawl files opened from outside the data folder.</param>
/// <param name="Removed">Paths removed from the library (not from disk), until they are restored or opened again.</param>
internal sealed record LibraryFile(IReadOnlyList<OpenedCrawl> Opened, IReadOnlyList<string> Removed);
