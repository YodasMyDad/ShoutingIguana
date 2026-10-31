using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>Logs notices and keeps a misbehaving observer (UI code) from taking the crawl down.</summary>
internal sealed class SafeCrawlObserver(ICrawlObserver inner, ILogger logger) : ICrawlObserver
{
    public void OnProgress(CrawlProgress progress)
    {
        try
        {
            inner.OnProgress(progress);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Crawl observer failed while handling progress");
        }
    }

    public void OnNotice(CrawlNotice notice)
    {
        var level = notice.Level switch
        {
            CrawlNoticeLevel.Error => LogLevel.Error,
            CrawlNoticeLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        logger.Log(level, "Crawl notice {Code}: {Message}", notice.Code, notice.Message);

        try
        {
            inner.OnNotice(notice);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Crawl observer failed while handling notice {Code}", notice.Code);
        }
    }
}
