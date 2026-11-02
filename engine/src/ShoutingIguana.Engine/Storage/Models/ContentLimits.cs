namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The limits a page is judged against: the last analysis's, or the engine's defaults before the first.</summary>
public sealed record ContentLimits(
    int TitleMin,
    int TitleMax,
    int DescriptionMin,
    int DescriptionMax,
    int MinWords,
    int SlowResponseMs,
    long LargePageBytes,
    bool FromAnalysis);
