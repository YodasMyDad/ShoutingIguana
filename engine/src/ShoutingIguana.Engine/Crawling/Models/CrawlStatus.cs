namespace ShoutingIguana.Engine.Crawling.Models;

public enum CrawlStatus : byte
{
    Running = 0,
    Paused = 1,
    Stopped = 2,
    Completed = 3,
    Failed = 4
}
