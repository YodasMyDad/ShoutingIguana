using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// One run's progress, as this sidecar watched it: every progress event, downsampled to at most <see cref="CAPACITY"/>
/// points spread evenly since the run started (each half second at first, then each second, two seconds...), so a UI
/// that opens mid-crawl can draw it from the start. Memory stays flat however long the crawl runs. Progress arrives on
/// the engine's threads and requests read it on theirs.
/// </summary>
internal sealed class ProgressHistory(int capacity = ProgressHistory.CAPACITY)
{
    public const int CAPACITY = 600;

    private readonly Lock _sync = new();
    private readonly DownsampledSeries<HistoryPointDto> _points = new(capacity);

    public void Add(DateTime at, CrawlProgress progress)
    {
        var point = HistoryPointDto.Live(at, progress);
        lock (_sync)
        {
            _points.Add(point);
        }
    }

    /// <summary>Oldest first, the latest last.</summary>
    public IReadOnlyList<HistoryPointDto> Points()
    {
        lock (_sync)
        {
            return _points.Points();
        }
    }
}
