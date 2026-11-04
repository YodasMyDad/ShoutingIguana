namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// Thresholds the rules use. The defaults follow common SEO tool defaults; each analysis stores the values it ran
/// with in the crawl file. Title and description lengths count user-perceived characters, with full-width (CJK)
/// characters and emoji counted as two because they take about twice the room in search results.
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
    /// Differing bits out of the 256 of two pages' content fingerprints (<see cref="Parsing.ContentFingerprint"/>) at which
    /// they count as near duplicates. Each bit differs with probability θ/π, where cos θ is how alike the pages' 3-word
    /// shingles are, so d bits apart estimates a similarity of cos(πd/256): 24 is about 96% the same (a one-word edit in a
    /// 300-word page is about 12 bits; unrelated pages are about 128 apart). Pages that much alike are found half the
    /// time, and pages 98% alike 98% of the time. Unrelated pages come within 24 bits once in 3×10^43 pairs, never at any
    /// crawl size; pages sharing half their text, once in 5×10^18; 60%, once in 2×10^14 (0.002 pairs on a million pages
    /// that all share it).
    /// </summary>
    /// <remarks>
    /// A new name for a new scale: analyses before it stored differing bits out of 64 (<c>nearDuplicateMaxDistance</c>, 6),
    /// which this engine never reads, so such settings take this default.
    /// </remarks>
    public int NearDuplicateMaxBits { get; init; } = 24;

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

        // Every 4 bits more add a block to each lane's search, with more and less selective keys; past 32 bits (92% alike)
        // "near" stops meaning much anyway.
        if (NearDuplicateMaxBits is < 0 or > 32)
        {
            errors.Add("Near-duplicate bits must be between 0 and 32 of 256.");
        }

        if (MinWordCount < 0 || DuplicateMinWordCount < 0 || ImageAltMaxLength < 1 || LargeImageBytes < 1 ||
            LargePageBytes < 1 || SlowResponseMs < 1 || MaxUrlLength < 1)
        {
            errors.Add("Word counts must not be negative, and size, time and length limits must be positive.");
        }

        return errors;
    }
}
