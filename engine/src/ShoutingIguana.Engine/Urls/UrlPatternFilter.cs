using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// Which discovered URLs are fetched. An exclude match is left out. When any include pattern is set, a URL that
/// matches none of them is left out too. Empty lists fetch every URL the rest of the crawl would. A pattern that
/// doesn't compile fetches every URL: the options are refused before a crawl starts.
/// </summary>
internal sealed class UrlPatternFilter
{
    private readonly UrlPatterns _include;
    private readonly UrlPatterns _exclude;

    public UrlPatternFilter(CrawlOptions options)
    {
        UrlPatterns.TryCompile(options.IncludePatterns, out var include, out _);
        UrlPatterns.TryCompile(options.ExcludePatterns, out var exclude, out _);
        _include = include;
        _exclude = exclude;
    }

    /// <summary>False when the URL matches an exclude pattern, or misses every include pattern when includes are set.</summary>
    public bool Allows(string url) =>
        !_exclude.Matches(url, whenUnsure: true) && (!_include.Any || _include.Matches(url, whenUnsure: false));
}
