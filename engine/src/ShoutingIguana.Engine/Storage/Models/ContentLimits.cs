namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The limits a URL is judged against: the last analysis's, or the engine's defaults before the first.</summary>
/// <param name="TitleMinPixels">A title narrower than this, in pixels, is too short (<c>title-too-short</c>).</param>
/// <param name="TitleMaxPixels">A title wider than this, in pixels, is too long (<c>title-too-long</c>).</param>
/// <param name="DescriptionMinPixels">A meta description narrower than this, in pixels, is too short.</param>
/// <param name="DescriptionMaxPixels">A meta description wider than this, in pixels, is too long.</param>
/// <param name="LargeImageBytes">An image bigger than this is too large (<c>image-too-large</c>).</param>
/// <param name="ImageAltMaxLength">Alt text longer than this is too long (<c>image-alt-too-long</c>).</param>
/// <param name="MaxUrlLength">A URL longer than this is too long (<c>url-too-long</c>).</param>
public sealed record ContentLimits(
    int TitleMinPixels,
    int TitleMaxPixels,
    int DescriptionMinPixels,
    int DescriptionMaxPixels,
    int MinWords,
    int SlowResponseMs,
    long LargePageBytes,
    long LargeImageBytes,
    int ImageAltMaxLength,
    int MaxUrlLength,
    bool FromAnalysis);
