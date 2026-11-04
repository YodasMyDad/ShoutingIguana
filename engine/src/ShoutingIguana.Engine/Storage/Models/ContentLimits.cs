namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The limits a URL is judged against: the last analysis's, or the engine's defaults before the first.</summary>
/// <param name="LargeImageBytes">An image bigger than this is too large (<c>image-too-large</c>).</param>
/// <param name="ImageAltMaxLength">Alt text longer than this is too long (<c>image-alt-too-long</c>).</param>
/// <param name="MaxUrlLength">A URL longer than this is too long (<c>url-too-long</c>).</param>
public sealed record ContentLimits(
    int TitleMin,
    int TitleMax,
    int DescriptionMin,
    int DescriptionMax,
    int MinWords,
    int SlowResponseMs,
    long LargePageBytes,
    long LargeImageBytes,
    int ImageAltMaxLength,
    int MaxUrlLength,
    bool FromAnalysis);
