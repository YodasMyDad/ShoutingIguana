namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// One stretch of a run's crawling, as the run's clock has it at some moment: from the run's start, or its resuming after a
/// pause, to its next pause or its end. The writer keeps the latest it hears of each.
/// </summary>
/// <param name="Index">Which of the run's stretches: 0 is its first.</param>
/// <param name="StartedAt">When it began, to the millisecond.</param>
/// <param name="CrawledMs">How long it has gone on, by a clock that only runs forward.</param>
/// <param name="Ended">It has ended: the crawl was paused, or the run is over.</param>
internal readonly record struct RunStretch(int Index, DateTimeOffset StartedAt, long CrawledMs, bool Ended);
