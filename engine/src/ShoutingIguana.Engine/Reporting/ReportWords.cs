using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// The report's own words, which aren't any check's: what it calls each severity, who usually fixes a thing, how it says a
/// count and a date. One document serves both readers, so the plain word leads and the technical one is beside it
/// ("Serious" is the checks' "Error").
/// </summary>
internal static class ReportWords
{
    private static readonly CultureInfo NUMBERS = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>A severity as the report names it: "Serious", "Worth fixing", "Worth a look".</summary>
    public static string Severity(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => "Serious",
        IssueSeverity.Warning => "Worth fixing",
        _ => "Worth a look"
    };

    /// <summary>A severity as the checks name it: "Error", "Warning", "Notice".</summary>
    public static string Technical(IssueSeverity severity) => severity.ToString();

    /// <summary>"2 serious problems", "1 problem worth fixing", "13 things worth a look".</summary>
    public static string Counted(IssueSeverity severity, long count) => severity switch
    {
        IssueSeverity.Error => Plural(count, "serious problem", "serious problems"),
        IssueSeverity.Warning => Plural(count, "problem worth fixing", "problems worth fixing"),
        _ => Plural(count, "thing worth a look", "things worth a look")
    };

    /// <summary>What stands where a share would, for a check reported once for the whole site: "1 of 265 pages" would say the fault is one page's.</summary>
    public const string CHECKED_ONCE = "Checked once, for the whole site";

    /// <summary>Who usually makes a fix, after "usually fixed by".</summary>
    public static string FixedBy(IssueFixer fixer) => fixer switch
    {
        IssueFixer.You => "you, in your site’s editor",
        IssueFixer.Developer => "your developer or hosting company",
        _ => "you or your developer"
    };

    /// <summary>The score's word.</summary>
    public static string Band(ScoreBand band) => band switch
    {
        ScoreBand.VeryGood => "Very good",
        ScoreBand.Good => "Good",
        ScoreBand.NeedsWork => "Needs work",
        _ => "Poor"
    };

    /// <summary>The summary's first sentence, by the score's word.</summary>
    public static string Lead(ScoreBand band) => band switch
    {
        ScoreBand.VeryGood => "This site is in very good shape.",
        ScoreBand.Good => "This site is in good shape.",
        ScoreBand.NeedsWork => "This site needs some work.",
        _ => "This site has problems that will hold it back in search."
    };

    /// <summary>How much of the site, as the breakdown says it: "a few", "the whole site".</summary>
    public static string Extent(ScoreExtent extent) => extent switch
    {
        ScoreExtent.Few => "a few",
        ScoreExtent.Some => "some",
        ScoreExtent.Many => "many",
        ScoreExtent.Most => "most",
        _ => "the whole site"
    };

    /// <summary>"5 of 265 pages", "9 of 4,521 addresses"; for a check reported once for the whole site, that it was.</summary>
    public static string CountOf(ScoreLine line, IssueDefinition check) => check.ReportedOnce || line.Unit == ScoreUnit.Site
        ? CHECKED_ONCE
        : line.Unit == ScoreUnit.Pages
            ? $"{Number(line.Count)} of {Plural(line.Of, "page", "pages")}"
            : $"{Number(line.Count)} of {Plural(line.Of, "address", "addresses")}";

    /// <summary>A check's count where there is no score to say what it's a share of: "5 pages", "9 addresses".</summary>
    public static string CountOf(long count, IssueDefinition check) => check.ReportedOnce
        ? CHECKED_ONCE
        : check.Subject == IssueSubject.Page ? Plural(count, "page", "pages") : Plural(count, "address", "addresses");

    /// <summary>A count with its thousands apart, as the UK writes them: 4,521.</summary>
    public static string Number(long number) => number.ToString("N0", NUMBERS);

    /// <summary>"1 page", "265 pages".</summary>
    /// <summary>The most characters of a title or description Simple quotes before where Google will likely cut it.</summary>
    public const int CUT_QUOTE_MAX = 30;

    /// <summary>
    /// Where Google will likely cut a page's title or description, as Simple says it (the app's plainEvidence words it the
    /// same): "Google will likely cut this title off after “…Garden Sofa”". The quote is the last whole words that show, up
    /// to <see cref="CUT_QUOTE_MAX"/> characters, opening with "…" when more comes before them; a single word longer than
    /// that is quoted by its end. Null when nothing is cut.
    /// </summary>
    /// <param name="what">"title" or "description".</param>
    /// <param name="inResults">The text as results show it: the <c>titleInResults</c> column, ending "…" where it's cut.</param>
    public static string? CutOff(string what, string? inResults)
    {
        if (inResults is null || !inResults.EndsWith(UrlComputedColumns.CUT_MARK, StringComparison.Ordinal))
        {
            return null;
        }

        var shown = inResults[..^UrlComputedColumns.CUT_MARK.Length].TrimEnd();
        var tail = shown;
        if (new StringInfo(shown).LengthInTextElements > CUT_QUOTE_MAX)
        {
            var words = shown.Split(' ');
            tail = words[^1];
            for (var index = words.Length - 2; index >= 0 && new StringInfo($"{words[index]} {tail}").LengthInTextElements <= CUT_QUOTE_MAX; index--)
            {
                tail = $"{words[index]} {tail}";
            }

            var characters = new StringInfo(tail);
            if (characters.LengthInTextElements > CUT_QUOTE_MAX)
            {
                tail = characters.SubstringByTextElements(characters.LengthInTextElements - CUT_QUOTE_MAX);
            }
        }

        var more = tail.Length < shown.Length ? UrlComputedColumns.CUT_MARK : string.Empty;
        return $"Google will likely cut this {what} off after “{more}{tail}”";
    }

    /// <summary>A title or description too short, as Simple says it: "Only 5 characters long".</summary>
    public static string OnlyCharacters(long characters) => $"Only {Plural(characters, "character", "characters")} long";

    public static string Plural(long count, string one, string many) => $"{Number(count)} {(count == 1 ? one : many)}";

    /// <summary>"31 October 2026".</summary>
    public static string Date(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"14:13".</summary>
    public static string Time(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"37 min 25 s", "1 h 2 min", "48 s": the two largest units.</summary>
    public static string Duration(TimeSpan time)
    {
        var seconds = (long)Math.Round(time.TotalSeconds);
        var (hours, minutes, rest) = (seconds / 3_600, seconds % 3_600 / 60, seconds % 60);
        return hours > 0 ? $"{Number(hours)} h {minutes} min" : minutes > 0 ? $"{minutes} min {rest} s" : $"{rest} s";
    }
}
