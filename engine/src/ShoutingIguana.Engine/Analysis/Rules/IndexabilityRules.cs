using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Robots directives and meta refresh. Directives on a page that redirects with an instant meta refresh don't matter.</summary>
internal sealed class IndexabilityRules : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var robots = page.Facts.Robots;
        if (robots.NoIndex && !page.IsRedirect)
        {
            yield return new IssueHit(IssueCatalog.NOINDEX_PAGE, page.UrlId);
        }

        if (robots.NoFollow && !page.IsRedirect)
        {
            yield return new IssueHit(IssueCatalog.NOFOLLOW_PAGE, page.UrlId);
        }

        if (page.IsIndexable && robots.Directives.FirstOrDefault(BlocksSnippet) is { } directive)
        {
            yield return new IssueHit(IssueCatalog.SNIPPET_BLOCKED, page.UrlId, directive);
        }

        if (page.Facts.MetaRefresh is { } refresh)
        {
            var delay = refresh.DelaySeconds is { } seconds ? $" after {RuleText.Count(seconds, "second", "seconds")}" : string.Empty;
            yield return new IssueHit(IssueCatalog.META_REFRESH, page.UrlId,
                refresh.Url is { } target ? $"Moves to {target}{delay}" : $"Reloads the page{delay}");
        }
    }

    private static bool BlocksSnippet(string directive)
    {
        if (directive == "nosnippet")
        {
            return true;
        }

        var colon = directive.IndexOf(':');
        return colon > 0 && directive.AsSpan(0, colon).Trim().SequenceEqual("max-snippet") && directive.AsSpan(colon + 1).Trim().SequenceEqual("0");
    }
}
