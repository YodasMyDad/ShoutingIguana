using System.Globalization;
using ShoutingIguana.Cli.Output.Models;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output;

/// <summary>A comparison of two crawls in the terminal: how far they compare, the score, and a table of the checks that moved; or one check's rows and causes.</summary>
internal static class ComparisonPrinter
{
    /// <summary>The most lines a list of one check's rows or causes prints; <c>--json</c> lists them all.</summary>
    public const int LISTED = 50;

    private static readonly (string Header, Func<RowSplit, long> Count)[] SPLIT =
    [
        ("Fixed", split => split.Fixed), ("Gone", split => split.Gone), ("Not checked", split => split.NotChecked), ("Still", split => split.Still),
        ("New", split => split.New), ("New URL", split => split.NewUrl), ("Not compared", split => split.NotComparedThen)
    ];

    /// <summary>How far the two compare, the score then and now, the totals, and each check that moved, one line each.</summary>
    public static void Print(CrawlComparison comparison, string otherPath, TextWriter output)
    {
        PrintHeader(comparison, otherPath, output);
        if (comparison.Score is { } score)
        {
            var change = score.Change is { } points ? $", {points:+0;-0;0}" : " (worked out by another version of the score, so no change)";
            output.WriteLine($"Score: {Score(score.Then)} then, {Score(score.Now)} now{change}");
        }

        var totals = comparison.Totals;
        output.WriteLine($"Checks: {string.Join(", ", Enum.GetValues<CheckChangeState>().Select(state => $"{totals.Checks[state]:N0} {Word(state)}"))}");
        if (totals.Issues is { } issues)
        {
            output.WriteLine($"URLs: {string.Join(", ", SPLIT.Select(column => $"{column.Count(issues):N0} {Lower(column.Header)}"))}");
        }

        var moved = comparison.Checks.Where(check => check.State != CheckChangeState.Same || check.Rows is { } rows && rows.Still != check.Now).ToList();
        output.WriteLine();
        if (moved.Count == 0)
        {
            output.WriteLine("Nothing moved: every check flags what it did, on the same URLs.");
            return;
        }

        var width = Math.Max("Check".Length, moved.Max(check => check.CheckId.Length));
        var header = $"{"Check".PadRight(width)}  {"State",-15} {"Then",7} {"Now",7}";
        if (comparison.Comparable != Comparability.CountsOnly)
        {
            header += string.Concat(SPLIT.Select(column => $"  {column.Header.PadLeft(Width(column.Header))}"));
        }

        output.WriteLine(header);
        foreach (var check in moved)
        {
            var line = $"{Format.Printable(check.CheckId).PadRight(width)}  {Word(check.State),-15} {Number(check.Then),7} {Number(check.Now),7}";
            if (check.Rows is { } rows)
            {
                line += string.Concat(SPLIT.Select(column => $"  {column.Count(rows).ToString("N0", CultureInfo.CurrentCulture).PadLeft(Width(column.Header))}"));
            }

            output.WriteLine(line);
        }

        var same = comparison.Checks.Count - moved.Count;
        if (same > 0)
        {
            output.WriteLine($"  and {Format.Count(same, "check", "checks")} the same, on the same URLs.");
        }
    }

    /// <summary>One check: its change, then its rows now and then, and its causes, each list cut at <see cref="LISTED"/>.</summary>
    public static void Print(CrawlComparison comparison, string otherPath, string checkId, CheckComparison detail, TextWriter output)
    {
        PrintHeader(comparison, otherPath, output);
        output.WriteLine();
        if (detail.Check is not { } check)
        {
            output.WriteLine($"{Format.Printable(checkId)}: not checked in either crawl's analysis.");
            return;
        }

        output.WriteLine($"{Format.Printable(check.CheckId)}: {Word(check.State)}, {Number(check.Then)} then, {Number(check.Now)} now");
        if (check.Rows is { } split)
        {
            output.WriteLine($"  {string.Join(", ", SPLIT.Select(column => $"{column.Count(split):N0} {Lower(column.Header)}"))}");
        }

        List(output, "Flagged now", detail.Rows, row =>
            $"{(row.Change is { } change ? Word(change.State) : "-"),-16} {Format.Printable(row.Url)}{(row.Change?.DetailThen is { } then && then != row.Detail ? $"  (then: {Format.Printable(then)})" : string.Empty)}");
        List(output, "Flagged then, not now", detail.Cleared, row =>
            $"{Word(row.State),-16} {Format.Printable(row.Url)}{(row.Reason is { } reason ? $"  ({Word(reason.Code)}: {Format.Printable(reason.Text)})" : string.Empty)}");
        if (detail.Groups.Count > 0)
        {
            List(output, "Causes now", detail.Groups, group =>
                $"{(group.Change is { } change ? Word(change.State) : "-"),-8} {(group.Change is { } was ? Number(was.Then) : "-"),6} -> {group.Urls,-6:N0} {Cause(group.Detail, group.RelatedUrl)}");
        }

        if (detail.FixedGroups.Count > 0)
        {
            List(output, "Causes then with no URL now", detail.FixedGroups, group =>
                $"{group.Then:N0} then: {group.Fixed:N0} fixed, {group.Gone:N0} gone, {group.NotChecked:N0} not checked  {Cause(group.Detail, group.RelatedUrl)}");
        }
    }

    private static void PrintHeader(CrawlComparison comparison, string otherPath, TextWriter output)
    {
        output.WriteLine($"Compared with {Format.Printable(otherPath)}");
        output.WriteLine($"Comparable: {comparison.Comparable switch
        {
            Comparability.Full => "fully",
            Comparability.Partial => "in part: the crawls covered different things, so some URLs weren't checked on one side",
            _ => "by the counts only: no URL is matched"
        }}");
        foreach (var difference in comparison.Differences)
        {
            output.WriteLine($"  Differs: {Difference(difference)}");
        }

        if (comparison.PreviousNeedsAnalysis.Count > 0)
        {
            output.WriteLine($"  The other crawl's analysis isn't like this one's ({string.Join(", ", comparison.PreviousNeedsAnalysis.Select(Word))}): " +
                             $"run si analyze \"{otherPath}\" to compare like for like.");
        }
    }

    private static void List<T>(TextWriter output, string title, IReadOnlyList<T> items, Func<T, string> line)
    {
        output.WriteLine();
        output.WriteLine($"{title} ({items.Count:N0}):");
        foreach (var item in items.Take(LISTED))
        {
            output.WriteLine($"  {line(item)}");
        }

        if (items.Count > LISTED)
        {
            output.WriteLine($"  ... and {items.Count - LISTED:N0} more (--json lists them all)");
        }
    }

    private static string Difference(ComparisonDifference difference) => difference.Code switch
    {
        ComparisonDifferenceCode.StoppedEarly => $"stopped early: {difference.Side switch
        {
            ComparisonSide.Now => "this crawl",
            ComparisonSide.Then => "the other crawl",
            _ => "both crawls"
        }} didn't read all it set out to",
        ComparisonDifferenceCode.Coverage => $"coverage: {Value(difference.Then)} pages read then, {Value(difference.Now)} now",
        _ when difference.Then is null && difference.Now is null => $"{Word(difference.Code)} {difference.Field}",
        _ => $"{Word(difference.Code)}{(difference.Field is null ? string.Empty : " " + difference.Field)}: {Value(difference.Then)} then, {Value(difference.Now)} now"
    };

    private static string Value(System.Text.Json.Nodes.JsonNode? value) => value is null ? "none" : Format.Printable(value.ToJsonString());

    private static string Score(SiteScoreSummary score) => $"{score.Value} ({Word(score.Band)}{(score.Partial ? ", so far" : string.Empty)})";

    private static string Number(long? count) => count?.ToString("N0", CultureInfo.CurrentCulture) ?? "-";

    private static string Cause(string? detail, string? relatedUrl) =>
        string.Join("  ", new[] { relatedUrl, detail }.OfType<string>().Select(text => Format.Printable(text))) is { Length: > 0 } cause ? cause : "(no other URL)";

    /// <summary>A column a number wide enough for its header, and for the thousands of URLs a big crawl moves.</summary>
    private static int Width(string header) => Math.Max(header.Length, 6);

    /// <summary>A header in a sentence: "New URL" reads "new URL".</summary>
    private static string Lower(string header) => header.ToLowerInvariant().Replace(" url", " URL", StringComparison.Ordinal);

    /// <summary>An enum's name as words: <c>NotComparedThen</c> reads "not compared then", <c>NewUrl</c> "new URL".</summary>
    private static string Word<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Lower(string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? " " + character : character.ToString())));
}
