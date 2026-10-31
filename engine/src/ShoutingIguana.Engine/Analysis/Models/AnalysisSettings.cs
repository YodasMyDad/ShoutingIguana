namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// Thresholds the rules use. The defaults follow common SEO tool defaults; each analysis stores the values it ran
/// with in the crawl file.
/// </summary>
public sealed record AnalysisSettings
{
    public int TitleMinLength { get; init; } = 30;

    public int TitleMaxLength { get; init; } = 60;

    public int DescriptionMinLength { get; init; } = 70;

    public int DescriptionMaxLength { get; init; } = 155;

    /// <summary>Main-content words below which a page counts as thin.</summary>
    public int MinWordCount { get; init; } = 200;

    /// <summary>Pages with fewer main-content words are left out of duplicate content checks.</summary>
    public int DuplicateMinWordCount { get; init; } = 50;

    /// <summary>
    /// Differing bits out of 64 at which two content fingerprints count as near duplicates. Unrelated pages differ by
    /// about 32 bits; 6 catches text that is roughly 96% the same (a one-word edit in a long page is 1 to 5 bits) while
    /// a false match between unrelated pages stays around one in 10^11 pairs.
    /// </summary>
    public int NearDuplicateMaxDistance { get; init; } = 6;

    public int ImageAltMaxLength { get; init; } = 125;

    public long LargeImageBytes { get; init; } = 200 * 1024;

    public long LargePageBytes { get; init; } = 1024 * 1024;

    public int SlowResponseMs { get; init; } = 1_000;

    public int MaxUrlLength { get; init; } = 115;

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (TitleMinLength < 0 || TitleMaxLength <= TitleMinLength)
        {
            errors.Add("Title lengths must satisfy 0 <= minimum < maximum.");
        }

        if (DescriptionMinLength < 0 || DescriptionMaxLength <= DescriptionMinLength)
        {
            errors.Add("Description lengths must satisfy 0 <= minimum < maximum.");
        }

        // Each extra bit adds a bucketing pass with smaller buckets; beyond 8 bits "near" stops meaning much anyway.
        if (NearDuplicateMaxDistance is < 0 or > 8)
        {
            errors.Add("Near-duplicate distance must be between 0 and 8 bits.");
        }

        if (MinWordCount < 0 || DuplicateMinWordCount < 0 || ImageAltMaxLength < 1 || LargeImageBytes < 1 ||
            LargePageBytes < 1 || SlowResponseMs < 1 || MaxUrlLength < 1)
        {
            errors.Add("Word counts must not be negative, and size, time and length limits must be positive.");
        }

        return errors;
    }
}
