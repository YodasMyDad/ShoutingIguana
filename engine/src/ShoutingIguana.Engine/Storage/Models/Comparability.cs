namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How far two crawls can be compared (<see cref="Analysis.CrawlComparability"/>), served as <c>comparable</c>.</summary>
public enum Comparability
{
    /// <summary>Like for like: nothing differs but what's only noted (the client, the timeouts).</summary>
    Full,

    /// <summary>
    /// The crawls differ in what they covered (the start path, the scope, the limits, what was fetched, one stopped early,
    /// very different sizes): rows one of them didn't read read as not checked, or not compared.
    /// </summary>
    Partial,

    /// <summary>
    /// No URL can be matched (the start URL's scheme or host differs), or the analyses were made by other rules: each
    /// check's count then and now and the score are compared, and no row or cause is.
    /// </summary>
    CountsOnly
}
