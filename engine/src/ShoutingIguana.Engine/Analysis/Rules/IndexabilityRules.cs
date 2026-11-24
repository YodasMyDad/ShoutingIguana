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
            // A Refresh header works as the tag does, but it's set on the server, not in the page, so the detail says which.
            // A script that sent the browser on has neither: the detail names the script, as there is no tag to look for.
            var delay = refresh.DelaySeconds is { } seconds && !refresh.FromScript ? $" after {RuleText.Count(seconds, "second", "seconds")}" : string.Empty;
            var action = refresh.Url is { } target ? $"moves to {target}{delay}" : $"reloads the page{delay}";
            yield return new IssueHit(IssueCatalog.META_REFRESH, page.UrlId,
                refresh.FromScript ? $"A script {action} as the page loads"
                : refresh.FromHttpHeader ? $"The Refresh header {action}"
                : $"{char.ToUpperInvariant(action[0])}{action[1..]}");
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
