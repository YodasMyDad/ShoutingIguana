namespace ShoutingIguana.TestSite.Models;

public sealed record SyntheticSiteOptions
{
    /// <summary>Home page plus products (/p/1 ... /p/{PageCount-1}).</summary>
    public int PageCount { get; init; } = 1_000;

    /// <summary>Links in each product's content area, including next/previous/tree links.</summary>
    public int ContentLinksPerPage { get; init; } = 20;

    public int Seed { get; init; } = 42;

    /// <summary>Gzip HTML/XML/CSS/JS responses, as most real sites do.</summary>
    public bool Compress { get; init; } = true;
}
