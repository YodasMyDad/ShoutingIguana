namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A point in a recheck's run where tests make it fail (<see cref="Crawling.Models.RecheckRequest.AtStage"/>), to prove a
/// recheck that fails anywhere leaves the crawl file as it was.
/// </summary>
internal enum RecheckStage
{
    /// <summary>A robots.txt read again has been stored over the crawl's.</summary>
    RobotsStored,

    /// <summary>A sitemap read again has had the entries read before deleted, its new ones still to come.</summary>
    SitemapCleared,

    /// <summary>Some of its URLs are stored and others are still being fetched.</summary>
    Fetching,

    /// <summary>A URL fetched again is part-way stored: its old record gone and its new links in, its response not yet.</summary>
    PageStoring
}
