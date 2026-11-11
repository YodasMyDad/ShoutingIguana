using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output;

/// <summary>
/// Prints the score an analysis stored: its headline, as <c>si issues</c> leads with it, and the breakdown <c>si score</c>
/// gives. Nothing is worked out here but the words: every number is the analysis's.
/// </summary>
internal static class ScorePrinter
{
    /// <summary>"Score 85 of 100: Good", a partial one saying it is so far; or why there's no score.</summary>
    public static string Headline(AnalysisInfo analysis) => analysis.Score is { } score
        ? $"Score {score.Value} of 100{(score.Partial ? " so far" : string.Empty)}: {Word(score.Band)}"
        : $"No score: {NoScore(analysis.NoScoreReason ?? NoScoreReason.NotScored)}";

    /// <summary>The breakdown: what the score covers, a line for each check that took points off, the sum, and what fixing the first few would leave.</summary>
    /// <param name="output">Where to print it: the console.</param>
    public static void Print(AnalysisResults results, TextWriter output)
    {
        if (results.Score is not { } score)
        {
            output.WriteLine($"No score: {NoScore(results.NoScoreReason ?? NoScoreReason.NotScored)}");
            return;
        }

        var covers = score.Covers;
        output.WriteLine($"Score {score.Value} of 100{(score.Partial ? " so far" : string.Empty)}: {Word(score.Band)}");
        output.WriteLine($"  Covers {Format.Count(covers.Pages, "page", "pages")} and {Format.Count(covers.Urls, "URL", "URLs")}. " +
                          $"{covers.ChecksRun:N0} of {covers.ChecksRun + covers.ChecksSkipped:N0} checks ran" +
                          (covers.ChecksSkipped > 0 ? $"; {covers.ChecksSkipped:N0} couldn't." : "."));
        if (covers.Reason is { } reason)
        {
            output.WriteLine($"  {Incomplete(reason)}, so the score is of what it read so far.");
        }

        if (covers.BlockedByRobots > 0)
        {
            output.WriteLine($"  robots.txt kept the crawler out of {Format.Count(covers.BlockedByRobots, "URL", "URLs")}, which weren't read.");
        }

        var costly = score.Lines.Where(line => line.Points > 0).ToList();
        if (costly.Count > 0)
        {
            output.WriteLine();
            foreach (var line in costly)
            {
                var title = IssueCatalog.Find(line.CheckId)?.Title ?? line.CheckId;
                output.WriteLine($"  {line.Severity,-8} {Format.Printable(title),-46} {Share(line),-32} {-line.Points,4}   {Format.Printable(line.CheckId)}");
            }
        }

        var free = score.Lines.Count - costly.Count;
        if (free > 0)
        {
            output.WriteLine($"  Notices take nothing off: {Format.Count(free, "notice check", "notice checks")} found something.");
        }

        output.WriteLine();
        output.WriteLine($"  100 - {score.Lost} = {score.BeforeCeiling}{(score.Lost > 100 ? " (it can't go below 0)" : string.Empty)}");
        if (score.HeldBySerious)
        {
            output.WriteLine($"  Held at {score.Value} while an error check has issues ({score.BeforeCeiling} otherwise).");
        }

        if (score.IfFixed.CheckIds.Count > 0)
        {
            output.WriteLine($"  Fix first: {Format.Printable(string.Join(", ", score.IfFixed.CheckIds))}. Without {(score.IfFixed.CheckIds.Count == 1 ? "it" : "them")} the score would be {score.IfFixed.Value}.");
        }

        output.WriteLine($"  Score version {score.Version}.");
    }

    private static string Share(ScoreLine line) => line.Unit == ScoreUnit.Site
        ? "the whole site"
        : $"{line.Count:N0} of {Format.Count(line.Of, line.Unit == ScoreUnit.Pages ? "page" : "URL", line.Unit == ScoreUnit.Pages ? "pages" : "URLs")}: {Word(line.Extent)}";

    private static string Word(ScoreBand band) => band switch
    {
        ScoreBand.VeryGood => "Very good",
        ScoreBand.Good => "Good",
        ScoreBand.NeedsWork => "Needs work",
        _ => "Poor"
    };

    private static string Word(ScoreExtent extent) => extent switch
    {
        ScoreExtent.Few => "a few",
        ScoreExtent.Some => "some",
        ScoreExtent.Many => "many",
        ScoreExtent.Most => "most",
        _ => "the whole site"
    };

    private static string Incomplete(ScoreIncompleteReason reason) => reason switch
    {
        ScoreIncompleteReason.Stopped => "The crawl was stopped before it finished",
        ScoreIncompleteReason.Failed => "The crawl failed before it finished",
        ScoreIncompleteReason.UrlLimit => "The crawl's URL limit kept part of the site out",
        _ => "The crawl's depth limit kept part of the site out"
    };

    private static string NoScore(NoScoreReason reason) => reason == NoScoreReason.NoPages
        ? "the crawl read no page, so there is nothing to score."
        : "this analysis was made before the score. Analyse the crawl again (si analyze) to score it.";
}
