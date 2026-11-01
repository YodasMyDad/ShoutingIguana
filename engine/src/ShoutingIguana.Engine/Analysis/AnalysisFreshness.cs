using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Whether a crawl's last analysis still stands: the crawl hasn't changed since, this engine would judge it the same
/// way, and it would run with the same thresholds. Each analysis stores what it was run against for this.
/// </summary>
public static class AnalysisFreshness
{
    /// <summary>
    /// What an analysis computes and stores. Bump it with every change that alters what analysing the same crawl
    /// stores: a rule that judges differently, a changed threshold default, a new column in the URL summary. Analyses
    /// stored by another version are out of date (<see cref="OutOfDateReason.EngineChanged"/>), and the UI offers to
    /// run them again. A change to the catalog's checks is caught without a bump, by <see cref="ChecksFingerprint"/>.
    /// </summary>
    public const int VERSION = 1;

    /// <summary>The catalog's check ids with their categories and severities, hashed. Titles and texts don't count.</summary>
    public static string ChecksFingerprint { get; } = Fingerprint(IssueCatalog.All);

    /// <summary>Why the analysis no longer stands, or nothing when it does.</summary>
    /// <param name="crawlUpdatedAt">The crawl's <see cref="CrawlFileSummary.UpdatedAt"/> now.</param>
    /// <param name="settings">The thresholds an analysis would run with now.</param>
    public static IReadOnlyList<OutOfDateReason> Check(AnalysisInfo analysis, DateTimeOffset crawlUpdatedAt, AnalysisSettings settings)
    {
        List<OutOfDateReason> reasons = [];

        // Every run of a crawl stamps the crawl row when it starts and ends, and the analysis keeps the stamp it saw, so
        // comparing the two is exact whatever the clocks did. Engines that didn't keep it leave the times to compare.
        var crawlChanged = analysis.CrawlUpdatedAt is { } seen ? seen != crawlUpdatedAt : crawlUpdatedAt > analysis.AnalyzedAt;
        if (crawlChanged)
        {
            reasons.Add(OutOfDateReason.CrawlChanged);
        }

        if (analysis.AnalysisVersion != VERSION || analysis.ChecksFingerprint != ChecksFingerprint)
        {
            reasons.Add(OutOfDateReason.EngineChanged);
        }

        if (analysis.Settings is not { } used || Canonical(used) != Canonical(settings))
        {
            reasons.Add(OutOfDateReason.SettingsChanged);
        }

        return reasons;
    }

    /// <summary>
    /// Order-independent, so moving a check in the catalog changes nothing, while adding, removing or recategorising one,
    /// or changing its severity (which the issue counts are grouped by), does.
    /// </summary>
    internal static string Fingerprint(IEnumerable<IssueDefinition> checks)
    {
        var lines = checks.Select(check => $"{check.Id}\t{check.Category}\t{check.Severity}\n").Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines))))[..16];
    }

    /// <summary>
    /// The settings as the analysis stores them, so equal settings give equal text. Read back from a crawl file, a setting
    /// the analysing engine didn't have reads as 0 (the source-generated reader doesn't apply defaults), so it counts as
    /// different unless 0 is its default here: when in doubt, the analysis is out of date.
    /// </summary>
    private static string Canonical(AnalysisSettings settings) => JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings);
}
