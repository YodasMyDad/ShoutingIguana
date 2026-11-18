using System.Globalization;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Cli.Output;

/// <summary>
/// Prints a live progress line (redrawn in place in a terminal, or every few seconds when output is
/// redirected) and any notices the engine raises.
/// </summary>
internal sealed class ConsoleCrawlObserver(bool quiet) : ICrawlObserver
{
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private readonly Lock _sync = new();
    private long _lastPrintMs;
    private int _lastLineLength;

    public void OnProgress(CrawlProgress progress)
    {
        if (quiet)
        {
            return;
        }

        var now = Environment.TickCount64;
        var interval = _interactive ? 1_000 : 10_000;
        var isFinal = progress.Status is not (CrawlStatus.Running or CrawlStatus.Paused);
        if (!isFinal && now - Interlocked.Read(ref _lastPrintMs) < interval)
        {
            return;
        }

        Interlocked.Exchange(ref _lastPrintMs, now);

        // A recheck's run counts its own fetches: those it has stored of those it has planned so far. The crawl's own counts
        // (its errors, its blocked URLs) aren't the recheck's.
        var line = progress.Recheck is { } recheck
            ? string.Create(CultureInfo.InvariantCulture,
                $"[{Format.Duration(progress.Elapsed)}] Rechecking: {recheck.Done:N0} of {recheck.Total:N0} URLs fetched{(recheck is { Phase: 2, Total: > 0 } ? " (the pages linking to those still failing)" : string.Empty)}")
            : string.Create(CultureInfo.InvariantCulture,
                $"[{Format.Duration(progress.Elapsed)}] {progress.Fetched:N0} fetched ({progress.PagesPerSecond:N0}/s) | {progress.HtmlPages:N0} pages | {progress.Queued:N0} queued | {progress.InFlight} in flight | {progress.Errors:N0} errors | {progress.Blocked:N0} blocked | {Format.Bytes(progress.BytesDownloaded)}");

        // A section the site limits apart is read one page at a time: said plainly, with what is left and when it goes on.
        foreach (var wait in progress.Waits.Where(wait => wait is { Reason: CrawlWaitReason.SectionLimited, Duration: not null }))
        {
            var seconds = wait.Duration!.Value.TotalSeconds;
            var next = seconds < 120
                ? string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds):0} s")
                : string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds / 60):0} min");
            line += string.Create(CultureInfo.InvariantCulture, $" | {wait.Host} limits {wait.Section} pages: {wait.Urls ?? 0:N0} left, next try in {next}");
        }

        lock (_sync)
        {
            if (_interactive)
            {
                Console.Write('\r' + line.PadRight(_lastLineLength));
                _lastLineLength = line.Length;
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }

    public void OnNotice(CrawlNotice notice)
    {
        lock (_sync)
        {
            EndProgressLine();
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = notice.Level switch
            {
                CrawlNoticeLevel.Error => ConsoleColor.Red,
                CrawlNoticeLevel.Warning => ConsoleColor.Yellow,
                _ => previous
            };
            Console.WriteLine($"{notice.Level.ToString().ToUpperInvariant()}: {Format.Printable(notice.Message)}");
            Console.ForegroundColor = previous;
        }
    }

    public void EndProgressLine()
    {
        lock (_sync)
        {
            if (_interactive && _lastLineLength > 0)
            {
                Console.WriteLine();
                _lastLineLength = 0;
            }
        }
    }
}
