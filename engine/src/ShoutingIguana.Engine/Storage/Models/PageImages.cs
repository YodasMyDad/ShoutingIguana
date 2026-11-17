namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The images a page shows (<see cref="PageImagesReader.Read"/>): each image file once, the largest first.</summary>
/// <param name="Total">How many image files the page shows, each once.</param>
/// <param name="Links">The page's image links: an image shown twice is two.</param>
/// <param name="Rows">The first of them, the largest first.</param>
public sealed record PageImages(int UrlId, long Total, long Links, IReadOnlyList<PageImage> Rows)
{
    /// <summary>
    /// Whether the crawl measured images' pixel sizes (<see cref="CrawlSchema.PIXEL_COLUMNS"/>): false for a crawl made before
    /// they were read, and not continued since, whose images' widths and heights are all unknown.
    /// </summary>
    public bool PixelSizes { get; init; } = true;
}
