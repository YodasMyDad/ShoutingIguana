namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// Thresholds the rules use. The defaults follow common SEO tool defaults; each analysis stores the values it ran
/// with in the crawl file. Title and description widths are in pixels, as Google's desktop results show them (Arial at
/// 20 px and 14 px, <see cref="Rules.SearchResultWidth"/>).
/// </summary>
/// <remarks>
/// The four widths took new names when they went from characters to pixels (<c>titleMaxLength</c> became
/// <c>titleMaxPixels</c>), so a limit saved in characters is never read as pixels: <see cref="LegacyAnalysisSettings"/>
/// converts the old names where saved settings and crawl files are read.
/// </remarks>
public sealed record AnalysisSettings
{
    /// <summary>How wide an ordinary title's character is, on average, in pixels: for a limit's characters hint, and to convert a limit saved in characters.</summary>
    public const double TYPICAL_TITLE_CHARACTER_PX = 9.4;

    /// <summary>How wide an ordinary description's character is, on average, in pixels.</summary>
    public const double TYPICAL_DESCRIPTION_CHARACTER_PX = 6.2;

    /// <summary>A title narrower than this, in pixels, is too short: about 20 characters.</summary>
    public int TitleMinPixels { get; init; } = 200;

    /// <summary>A title wider than this, in pixels, will likely be cut off in Google's desktop results: about 60 characters.</summary>
    public int TitleMaxPixels { get; init; } = 580;

    /// <summary>A meta description narrower than this, in pixels, is too short: about 65 characters.</summary>
    public int DescriptionMinPixels { get; init; } = 400;

    /// <summary>A meta description wider than this, in pixels, will likely be cut off: about 160 characters.</summary>
    public int DescriptionMaxPixels { get; init; } = 985;

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

    /// <summary>An indexable page linking to fewer other URLs of the site than this is a dead end. 0 turns the check off.</summary>
    public int MinInternalOutlinks { get; init; } = 3;

    /// <summary>An indexable page more clicks than this from the start is deep.</summary>
    public int MaxClickDepth { get; init; } = 4;

    /// <summary>An indexable page with more internal links than this has very many.</summary>
    public int MaxInternalLinks { get; init; } = 1_000;

    /// <summary>An indexable page with more links to other sites than this has very many.</summary>
    public int MaxExternalLinks { get; init; } = 100;

    /// <summary>A page on another site that took longer than this to answer is slow.</summary>
    public int SlowExternalLinkMs { get; init; } = 3_000;

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (TitleMinPixels < 0 || TitleMaxPixels <= TitleMinPixels)
        {
            errors.Add("Title widths must satisfy 0 <= minimum < maximum.");
        }

        if (DescriptionMinPixels < 0 || DescriptionMaxPixels <= DescriptionMinPixels)
        {
            errors.Add("Description widths must satisfy 0 <= minimum < maximum.");
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

        if (MinInternalOutlinks < 0 || MaxClickDepth < 1 || MaxInternalLinks < 1 || MaxExternalLinks < 1 || SlowExternalLinkMs < 1)
        {
            errors.Add("The fewest links must not be negative, and the most clicks, links and the slowest link must be positive.");
        }

        return errors;
    }
}
