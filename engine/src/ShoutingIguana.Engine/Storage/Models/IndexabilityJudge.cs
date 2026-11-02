namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Who judged a URL's indexability.</summary>
public enum IndexabilityJudge
{
    /// <summary>The analysis, through the URL summary.</summary>
    Analysis,

    /// <summary>
    /// The crawl, without a URL summary: the analysis's judgement where its last run saw the URL
    /// (<c>effective_indexability</c>), otherwise what the crawler judged when it fetched it.
    /// </summary>
    Crawl
}
