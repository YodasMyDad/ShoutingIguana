namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Where the installed browser the crawl opens keeps its profile between crawls, so a human check passed once is not
/// asked again while its cookie lasts. It is the crawler's own folder, never the person's own browser profile.
/// </summary>
public static class BrowserProfile
{
    /// <summary>
    /// The program that hosts the engine sets this once, at its start. Null, the default, keeps a profile for each
    /// crawl and deletes it after.
    /// </summary>
    public static string? Folder { get; set; }
}
