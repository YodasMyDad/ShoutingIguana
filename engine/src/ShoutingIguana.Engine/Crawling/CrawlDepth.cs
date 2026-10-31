namespace ShoutingIguana.Engine.Crawling;

public static class CrawlDepth
{
    /// <summary>Depth of URLs known only from sitemaps. Sorts them after linked URLs in the frontier.</summary>
    public const int UNLINKED = 1_000_000;

    public static int Next(int depth) => depth >= UNLINKED ? UNLINKED : depth + 1;
}
