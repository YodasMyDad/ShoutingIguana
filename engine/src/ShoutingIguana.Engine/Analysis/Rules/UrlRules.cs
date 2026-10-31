using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>URL hygiene on indexable pages: the URLs that show up in search results.</summary>
internal sealed class UrlRules(AnalysisSettings settings) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var path = page.Uri.AbsolutePath;
        if (HasUppercase(path))
        {
            yield return new IssueHit(IssueCatalog.URL_UPPERCASE, page.UrlId);
        }

        if (path.Contains('_'))
        {
            yield return new IssueHit(IssueCatalog.URL_UNDERSCORES, page.UrlId);
        }

        if (page.Url.Length > settings.MaxUrlLength)
        {
            yield return new IssueHit(IssueCatalog.URL_TOO_LONG, page.UrlId, RuleText.Characters(page.Url.Length));
        }
    }

    /// <summary>Percent-escapes are written in uppercase hex by convention (%2F), so they don't count.</summary>
    private static bool HasUppercase(string path)
    {
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] == '%')
            {
                index += 2;
            }
            else if (char.IsAsciiLetterUpper(path[index]))
            {
                return true;
            }
        }

        return false;
    }
}
