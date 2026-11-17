using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl the library lists, with the cached summary its item was made from: all that choosing its previous crawl reads.</summary>
/// <param name="Summary">Null when the file couldn't be read, or has never been seen.</param>
internal sealed record LibraryEntry(CrawlListItem Item, CrawlFileSummary? Summary);
