using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;
using ShoutingIguana.Cli.Benchmarking;
using ShoutingIguana.Cli.Benchmarking.Models;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// Crawls a generated site of a given size and reports throughput, memory and correctness. With
/// thresholds set it fails the build, which is how CI holds the line on speed and memory.
/// </summary>
internal static class BenchCommand
{
    public static Command Create()
    {
        var pages = new Option<int>("--pages") { Description = "Synthetic site size.", DefaultValueFactory = _ => 100_000 };
        var concurrency = new Option<int>("--concurrency", "-c") { Description = "Crawler concurrency.", DefaultValueFactory = _ => 16 };
        var maxPeak = new Option<double>("--max-peak-mb") { Description = "Fail if the peak working set exceeds this (0 = no check)." };
        var maxGrowth = new Option<double>("--max-growth-mb") { Description = "Fail if memory grows more than this between early and late crawl (0 = no check)." };
        var maxGrowthPerUrl = new Option<double>("--max-growth-bytes-per-url") { Description = "Fail if memory grows by more than this per URL fetched, a leak (0 = no check)." };
        var minRate = new Option<double>("--min-pages-per-sec") { Description = "Fail if throughput is lower (0 = no check)." };
        var json = new Option<string?>("--json") { Description = "Write the results as JSON to this file." };
        var keep = new Option<bool>("--keep") { Description = "Keep the crawl file instead of deleting it." };

        var command = new Command("bench", "Benchmark the crawler against a generated site.")
        {
            pages, concurrency, maxPeak, maxGrowth, maxGrowthPerUrl, minRate, json, keep
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var pageCount = parse.GetValue(pages);
            var workers = parse.GetValue(concurrency);
            Console.WriteLine($"Starting a synthetic site with {pageCount:N0} pages...");
            using var site = await ChildSiteProcess.StartAsync(pageCount, cancellationToken);

            var databasePath = Path.Combine(Path.GetTempPath(), $"si-bench-{Guid.NewGuid():N}.sicrawl");
            var options = new CrawlOptions { StartUrl = site.BaseUri.ToString(), Concurrency = workers, RequestTimeoutSeconds = 30 };
            using var loggerFactory = Logging.Create(verbose: false);
            var observer = new ConsoleCrawlObserver(quiet: false);

            var keepCrawl = parse.GetValue(keep);
            try
            {
                CrawlSummary summary;
                IReadOnlyList<MemorySample> samples;
                await using (var job = new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, databasePath), observer))
                {
                    var sampler = new MemorySampler(TimeSpan.FromMilliseconds(250), () => job.Progress.Fetched);
                    await using var stopOnCancel = cancellationToken.Register(() => job.StopAsync());
                    summary = await job.Completion;
                    await sampler.DisposeAsync();
                    samples = sampler.Samples;
                }

                observer.EndProgressLine();
                AnalysisSummary? analysis = null;
                IReadOnlyList<MemorySample> analysisSamples = [];
                if (summary.Status == CrawlStatus.Completed)
                {
                    Console.WriteLine("Analysing...");
                    var sampler = new MemorySampler(TimeSpan.FromMilliseconds(250), () => 0);
                    try
                    {
                        analysis = await new CrawlAnalyzer(loggerFactory).AnalyzeAsync(databasePath, cancellationToken: cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Ctrl+C, handled below like one during the crawl.
                    }

                    await sampler.DisposeAsync();
                    analysisSamples = sampler.Samples;
                }

                // A stopped run measured nothing worth reporting, and says so the way si crawl does.
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Stopped before the benchmark finished.");
                    return 130;
                }

                var result = Evaluate(parse.GetValue(maxPeak), parse.GetValue(maxGrowth), parse.GetValue(maxGrowthPerUrl), parse.GetValue(minRate),
                    pageCount, workers, site, summary, samples, analysis, analysisSamples, databasePath);
                Print(result);

                if (parse.GetValue(json) is { } jsonPath)
                {
                    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(result, CliJsonContext.Default.BenchResult), CancellationToken.None);
                }

                if (keepCrawl)
                {
                    Console.WriteLine($"Crawl kept at {databasePath}");
                }

                return result.Failures.Count == 0 ? 0 : 1;
            }
            finally
            {
                // Also when the run fails or is cancelled part-way: at 100k pages the crawl is about 400 MB.
                if (!keepCrawl)
                {
                    foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-lock" })
                    {
                        File.Delete(file);
                    }
                }
            }
        });

        return command;
    }

    private static BenchResult Evaluate(double maxPeakMb, double maxGrowthMb, double maxGrowthBytesPerUrl, double minPagesPerSecond, int pageCount,
        int concurrency, ChildSiteProcess site, CrawlSummary summary, IReadOnlyList<MemorySample> samples,
        AnalysisSummary? analysis, IReadOnlyList<MemorySample> analysisSamples, string databasePath)
    {
        var expected = new SyntheticSite(new SyntheticSiteOptions { PageCount = pageCount }, site.BaseUri, site.ExternalBaseUri).GetExpectations();
        var stats = CrawlStatsReader.Read(databasePath);
        var mismatches = ExpectationCheck.Compare(stats, expected);
        if (analysis is not null)
        {
            mismatches.AddRange(expected.CompareIssues(
                IssueReader.ReadAll(databasePath).Select(row => (row.Issue, row.Url)),
                IssueCatalog.All.Select(issue => issue.Id),
                CrawlStatsReader.ReadSlowPages(databasePath, new AnalysisSettings().SlowResponseMs)));

            // As in the end-to-end test: every check the site supports ran, and none failed on a page. A rule that throws
            // on clean pages, or a check skipped by mistake, would otherwise pass, having flagged nothing extra.
            var ran = analysis.Issues.Select(issue => issue.Issue.Id).ToHashSet(StringComparer.Ordinal);
            var notRun = IssueCatalog.All.Select(issue => issue.Id).Where(id => !ran.Contains(id)).ToHashSet(StringComparer.Ordinal);
            if (!notRun.SetEquals(expected.SkippedChecks))
            {
                mismatches.Add($"Checks skipped: {Listed(notRun)}; expected {Listed(expected.SkippedChecks)}");
            }

            if (analysis.IsPartial != expected.SkippedChecks.Count > 0 || analysis.Notes.Count != (expected.SkippedChecks.Count > 0 ? 1 : 0))
            {
                mismatches.Add($"The analysis is {(analysis.IsPartial ? "partial" : "complete")}, with notes: {Listed(analysis.Notes)}");
            }
        }

        var total = Math.Max(1, summary.Progress.Fetched);
        var early = PeakWorkingSet(samples.Where(sample => sample.Fetched >= total * 0.1 && sample.Fetched <= total * 0.3));
        var late = PeakWorkingSet(samples.Where(sample => sample.Fetched >= total * 0.7));
        var peak = PeakWorkingSet(samples);
        var analysisPeak = PeakWorkingSet(analysisSamples);
        var peakManaged = samples.Count == 0 ? 0 : samples.Max(sample => sample.ManagedBytes);
        var seconds = Math.Max(0.001, summary.Progress.Elapsed.TotalSeconds);
        var pagesPerSecond = Math.Round(stats.HtmlPageCount / seconds, 1);
        var growth = Format.Megabytes(late - early);
        var growthPerUrl = GrowthPerUrl(samples, total);

        List<string> failures = [];
        if (summary.Status != CrawlStatus.Completed)
        {
            failures.Add($"Crawl ended as {summary.Status}: {summary.Error}");
        }

        failures.AddRange(mismatches);
        if (maxPeakMb > 0 && Format.Megabytes(peak) > maxPeakMb)
        {
            failures.Add($"Peak working set {Format.Megabytes(peak)} MB is over the {maxPeakMb} MB limit");
        }

        if (maxPeakMb > 0 && Format.Megabytes(analysisPeak) > maxPeakMb)
        {
            failures.Add($"Peak working set during analysis {Format.Megabytes(analysisPeak)} MB is over the {maxPeakMb} MB limit");
        }

        if (maxGrowthMb > 0 && growth > maxGrowthMb)
        {
            failures.Add($"Memory grew {growth} MB during the crawl, over the {maxGrowthMb} MB limit");
        }

        if (maxGrowthBytesPerUrl > 0 && growthPerUrl > maxGrowthBytesPerUrl)
        {
            failures.Add($"Memory grew {growthPerUrl:N0} bytes per URL fetched, over the {maxGrowthBytesPerUrl:N0} byte limit: a leak");
        }

        if (minPagesPerSecond > 0 && pagesPerSecond < minPagesPerSecond)
        {
            failures.Add($"{pagesPerSecond} pages/s is under the {minPagesPerSecond} pages/s floor");
        }

        return new BenchResult(
            pageCount, concurrency, summary.Status.ToString(), mismatches.Count == 0, expected.HtmlPages, stats.HtmlPageCount,
            summary.Progress.Fetched, stats.LinkCount, Math.Round(seconds, 1), pagesPerSecond, Format.Megabytes(peak),
            Format.Megabytes(peakManaged), Format.Megabytes(early), Format.Megabytes(late), growth, growthPerUrl,
            Format.Megabytes(new FileInfo(databasePath).Length), Math.Round(analysis?.Duration.TotalSeconds ?? 0, 1),
            analysis?.Issues.Sum(issue => issue.Count) ?? 0, Format.Megabytes(analysisPeak),
            $"{Environment.ProcessorCount} cores, {RuntimeInformation.OSDescription}",
            RuntimeInformation.FrameworkDescription, mismatches, failures);
    }

    private static string Listed(IEnumerable<string> values) => values.Any() ? string.Join(", ", values.Order(StringComparer.Ordinal)) : "none";

    private static long PeakWorkingSet(IEnumerable<MemorySample> samples) =>
        samples.Select(sample => sample.WorkingSetBytes).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Least-squares slope of the working set against URLs fetched, over the last 80% of the crawl (the first 20% is
    /// warm-up: caches and pools filling). Unlike comparing two peaks, an early spike can't hide later growth.
    /// </summary>
    private static double GrowthPerUrl(IReadOnlyList<MemorySample> samples, long total)
    {
        var window = samples.Where(sample => sample.Fetched >= total * 0.2).ToList();
        if (window.Count < 3)
        {
            return 0;
        }

        var meanFetched = window.Average(sample => (double)sample.Fetched);
        var meanBytes = window.Average(sample => (double)sample.WorkingSetBytes);
        var covariance = window.Sum(sample => (sample.Fetched - meanFetched) * (sample.WorkingSetBytes - meanBytes));
        var variance = window.Sum(sample => (sample.Fetched - meanFetched) * (sample.Fetched - meanFetched));
        return variance == 0 ? 0 : Math.Round(covariance / variance, 1);
    }

    private static void Print(BenchResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"Benchmark: {result.SyntheticPages:N0}-page synthetic site, concurrency {result.Concurrency} ({result.Machine})");
        Console.WriteLine($"  Result       {result.Status}, {(result.Correct ? "every planted issue found" : "MISMATCHES")}");
        Console.WriteLine($"  Crawled      {result.CrawledHtmlPages:N0} of {result.ExpectedHtmlPages:N0} pages, {result.Fetched:N0} URLs, {result.Links:N0} links in {result.ElapsedSeconds:N1} s");
        Console.WriteLine($"  Throughput   {result.PagesPerSecond:N0} pages/s");
        Console.WriteLine($"  Memory       peak {result.PeakWorkingSetMb:N0} MB working set (managed heap peak {result.PeakManagedMb:N0} MB)");
        Console.WriteLine($"  Flatness     {result.EarlyWorkingSetMb:N0} MB at 10-30% -> {result.LateWorkingSetMb:N0} MB at 70-100% ({result.GrowthMb:+0;-0} MB), {result.GrowthBytesPerUrl:N0} bytes per URL");
        Console.WriteLine($"  Database     {result.DatabaseMb:N0} MB");
        Console.WriteLine($"  Analysis     {result.AnalysisSeconds:N1} s, {result.IssuesFound:N0} issues found, peak {result.AnalysisPeakWorkingSetMb:N0} MB working set");

        foreach (var mismatch in result.Mismatches)
        {
            Console.WriteLine($"  Mismatch     {mismatch}");
        }

        foreach (var failure in result.Failures.Except(result.Mismatches))
        {
            Console.WriteLine($"  FAILED       {failure}");
        }
    }
}
