using System.CommandLine;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// <c>si recheck</c>: the fix loop's "Recheck these N pages". Fetches a check's URLs again (or a group's, or a list's)
/// into the same crawl file, analyses the crawl, and says what it found: "4 of 5 fixed".
/// </summary>
internal static class RecheckCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file: the site's latest crawl." };
        var check = new Option<string?>("--check") { Description = "The check whose flagged URLs to recheck, by id (si issues lists them)." };
        var groupUrl = new Option<string?>("--group-url") { Description = "Only the check's group about this URL: a broken target, an image, a duplicate cluster's first page." };
        var groupDetail = new Option<string?>("--group-detail") { Description = "Only the check's group with this detail." };
        var urls = new Option<string?>("--urls") { Description = "A file of URLs to recheck, one a line (up to 1,000), instead of the check's." };
        var headers = new Option<string[]>("--header") { Description = "A request header the crawl sent, given again: crawl files don't keep their values. Repeatable." };
        var proxy = new Option<string?>("--proxy") { Description = "The crawl's proxy, given again when it had a user name and password." };
        var planOnly = new Option<bool>("--plan") { Description = "Only say what the recheck would do (and how long it took to work out), fetching nothing." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Detailed engine logging." };
        var command = new Command("recheck", "Fetch a problem's URLs again into the same crawl, analyse it, and say how many are fixed.")
        {
            database, check, groupUrl, groupDetail, urls, headers, proxy, planOnly, verbose
        };

        command.SetAction((parse, cancellationToken) => CommandErrors.RunAsync(async () =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                return Fail($"No crawl file at {path}.");
            }

            var checkId = parse.GetValue(check);
            var listFile = parse.GetValue(urls);
            if (checkId is null && listFile is null)
            {
                return Fail("Name the check to recheck with --check, or the URLs with --urls.");
            }

            if (checkId is not null && IssueCatalog.Find(checkId) is null)
            {
                return Fail($"Unknown check '{checkId}'. si issues lists the checks.");
            }

            IssueGroupKey? group = null;
            if (parse.GetValue(groupUrl) is not null || parse.GetValue(groupDetail) is not null)
            {
                if (checkId is null || listFile is not null)
                {
                    return Fail("A group is one of a check's: give --check, and not --urls.");
                }

                int? relatedId = null;
                if (parse.GetValue(groupUrl) is { } text)
                {
                    relatedId = UrlGridReader.Lookup(path, text)?.Id ?? throw new InvalidOperationException($"The crawl has no URL {text}.");
                }

                group = new IssueGroupKey(parse.GetValue(groupDetail), relatedId);
            }

            List<int>? ids = null;
            if (listFile is not null)
            {
                ids = [];
                foreach (var line in File.ReadLines(listFile).Select(line => line.Trim()).Where(line => line.Length > 0))
                {
                    ids.Add(UrlGridReader.Lookup(path, line)?.Id ?? throw new InvalidOperationException($"The crawl has no URL {line}."));
                }
            }

            if (!CrawlCommand.TryParseHeaders(parse.GetValue(headers) ?? [], out var extraHeaders, out var headerError))
            {
                return Fail(headerError);
            }

            var resume = CrawlDatabase.ReadOptions(path).PlanResume(new ResumeChanges { ExtraHeaders = extraHeaders, ProxyUrl = parse.GetValue(proxy) });
            if (resume.MissingProxyCredentials)
            {
                return Fail("This crawl used a proxy with a user name and password, which crawl files don't store. Pass --proxy again.");
            }

            if (resume.MissingHeaders.Count > 0)
            {
                return Fail($"This crawl sent the header{(resume.MissingHeaders.Count == 1 ? string.Empty : "s")} {string.Join(", ", resume.MissingHeaders)}, " +
                            "whose values crawl files don't store. Give them again with --header, so the recheck asks the site as the crawl did.");
            }

            using var loggerFactory = Logging.Create(parse.GetValue(verbose));
            var settings = new AnalysisSettings();
            var ask = new RecheckAsk(checkId, group, ids);
            var planning = System.Diagnostics.Stopwatch.StartNew();
            var plan = RecheckPlanReader.Plan(path, ask, settings);
            if (parse.GetValue(planOnly))
            {
                Console.WriteLine($"{Planned(plan)} {plan.Judged:N0} judged, {plan.NotRechecked:N0} past the limit of {plan.Limit:N0}; " +
                                  $"about {Format.Duration(TimeSpan.FromMilliseconds(plan.EstimateMs))}{(plan.AnalysisFirst ? ", the analysis brought up to date first" : string.Empty)}" +
                                  $"{(plan.SuggestFullCheck ? "; checking the whole site again would take about as long" : string.Empty)}. Planned in {planning.ElapsedMilliseconds:N0} ms.");
                if (plan.Refused is { } refusal)
                {
                    Console.WriteLine($"It can't run: {refusal.Title}");
                }

                return 0;
            }

            if (plan.Refused is { } refused)
            {
                return Fail(refused.Title);
            }

            // The "before" it records is today's: an analysis out of date is brought up to date first.
            if (plan.AnalysisFirst)
            {
                Console.WriteLine("Updating the results first.");
                await new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, settings, cancellationToken);
                plan = RecheckPlanReader.Plan(path, ask, settings);
            }

            Console.WriteLine(Planned(plan));
            if (plan.Capped)
            {
                Console.WriteLine($"  The {plan.Judged - plan.NotRechecked:N0} most important of {plan.Judged:N0}; the other {plan.NotRechecked:N0} stay as the crawl found them.");
            }

            CrawlSummary run;
            await using (var job = new CrawlEngine(loggerFactory).Recheck(new RecheckRequest(path, plan, resume.Options)))
            {
                await using var stop = cancellationToken.Register(() => job.StopAsync());
                run = await job.Completion;
            }

            await new CrawlAnalyzer(loggerFactory).AnalyzeAsync(path, settings, CancellationToken.None);
            var recheck = RecheckReader.Read(path, run.RecheckId!.Value)!;
            Console.WriteLine(Describe(recheck));
            return run.Status == CrawlStatus.Failed ? 1 : 0;
        }));

        return command;
    }

    /// <summary>What the recheck will read: its URLs, then the pages linking to those still failing, and whatever else its check needs read again.</summary>
    internal static string Planned(RecheckPlan plan)
    {
        List<string> parts = [];
        if (plan.Kinds.Contains(RecheckKind.Urls))
        {
            parts.Add(Format.Count(plan.Phase1, "URL", "URLs") +
                      (plan.Phase2Max > 0 ? $", then up to {Format.Count(plan.Phase2Max, "page", "pages")} linking to those still failing" : string.Empty));
        }

        parts.AddRange(plan.Kinds.Where(kind => kind != RecheckKind.Urls).Select(kind => kind switch
        {
            RecheckKind.Sitemaps => "the sitemaps",
            RecheckKind.RobotsTxt => "robots.txt",
            _ => "the site's other versions"
        }));
        return $"Rechecking {string.Join(", and ", parts)}.";
    }

    /// <summary>The recheck's result in a line or two: "4 of 5 fixed. 1 still has it."</summary>
    internal static string Describe(RecheckRecord recheck)
    {
        if (recheck.Result is not { } result)
        {
            return $"Recheck {recheck.Id}: {recheck.Status}, not settled yet.";
        }

        var lines = new List<string>
        {
            recheck.CheckId is null
                ? $"Rechecked {Format.Count(recheck.Fetched, "URL", "URLs")}."
                : $"{result.Fixed:N0} of {recheck.Judged:N0} fixed.{(result.Still > 0 ? $" {result.Still:N0} still {(result.Still == 1 ? "has" : "have")} it." : string.Empty)}"
        };
        if (result.NotChecked > 0)
        {
            lines.Add($"  {Format.Count(result.NotChecked, "URL", "URLs")} couldn't be checked (an error, a block or a redirect in place of an answer the check can judge).");
        }

        if (result.NotRechecked > 0)
        {
            lines.Add($"  {Format.Count(result.NotRechecked, "URL wasn't", "URLs weren't")} rechecked{(recheck.Status == RecheckStatus.Completed ? " (past the limit of the most important 1,000)" : $" (the recheck {recheck.Status.ToString().ToLowerInvariant()})")}.");
        }

        if (result.New > 0)
        {
            lines.Add($"  {Format.Count(result.New, "URL it fetched has", "URLs it fetched have")} it now that didn't before.");
        }

        if (result.ScoreBefore is { } before && result.ScoreAfter is { } after)
        {
            lines.Add($"  Score {before} before, {after} after.");
        }

        lines.Add($"  {Format.Count(recheck.Fetched, "request", "requests")}{(recheck.CrawlingMs is { } ms ? $" in {Format.Duration(TimeSpan.FromMilliseconds(ms))}" : string.Empty)}. Other pages are as the crawl found them.");
        return string.Join(Environment.NewLine, lines);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
