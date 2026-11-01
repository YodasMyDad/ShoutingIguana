using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SpikeSidecar;

/// <summary>
/// A pretend crawl running at about 2,900 pages/s, computed from elapsed time so every SSE connection
/// (including one that reconnects) sees the same, continuous numbers.
/// </summary>
public sealed class CrawlSimulation
{
    private static readonly (string Code, string Message)[] Notices =
    [
        ("slow-host", "www.example-homeware.co.uk is answering slowly (p95 1.8 s). The crawl rate is reduced."),
        ("rate-limited", "The site answered 429 Too Many Requests. Backing off for 30 s."),
        ("robots-changed", "robots.txt changed during the crawl. New rules apply to URLs not yet fetched."),
        ("blocked", "12 requests in a row were refused (403). The site may be blocking the crawler."),
    ];

    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    public double ElapsedSeconds => Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;

    public int NoticeIndex(double seconds) => (int)(seconds / 10);

    public string ProgressJson(double t)
    {
        // pages/s = 2,900 plus two slow waves; crawled is its integral, so the counters and the rate agree.
        var rate = 2_900 + 250 * Math.Sin(t / 7) + 120 * Math.Sin(t / 2.3) + Jitter((long)(t * 2)) * 60;
        var crawled = (long)(2_900 * t - 250 * 7 * Math.Cos(t / 7) + 250 * 7 - 120 * 2.3 * Math.Cos(t / 2.3) + 120 * 2.3);
        var found = (long)(crawled * 1.35) + 5_000;
        var queued = found - crawled;
        var errors = (long)(crawled * 0.012);
        var s3 = (long)(crawled * (0.045 + 0.005 * Math.Sin(t / 11)));
        var s4 = (long)(crawled * 0.032);
        var s5 = (long)(crawled * 0.008);
        var s2 = crawled - s3 - s4 - s5;

        using var stream = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("t", Math.Round(t, 3));
            writer.WriteNumber("crawled", crawled);
            writer.WriteNumber("found", found);
            writer.WriteNumber("queued", queued);
            writer.WriteNumber("errors", errors);
            writer.WriteNumber("pagesPerSecond", Math.Round(rate, 1));
            writer.WriteStartObject("status");
            writer.WriteNumber("s2xx", s2);
            writer.WriteNumber("s3xx", s3);
            writer.WriteNumber("s4xx", s4);
            writer.WriteNumber("s5xx", s5);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public string NoticeJson(int noticeIndex)
    {
        var (code, message) = Notices[noticeIndex % Notices.Length];
        using var stream = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", noticeIndex);
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static double Jitter(long tick)
    {
        var z = (ulong)tick * 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)) * 2 - 1;
    }
}
