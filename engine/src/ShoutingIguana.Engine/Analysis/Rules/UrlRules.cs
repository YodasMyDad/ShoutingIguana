using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>URL hygiene on indexable pages: the URLs that show up in search results.</summary>
internal sealed class UrlRules(AnalysisSettings settings) : IPageRule
{
    private static readonly IdnMapping IDN = new();

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

        var length = RuleText.Length(DisplayUrl(page.Uri));
        if (length > settings.MaxUrlLength)
        {
            yield return new IssueHit(IssueCatalog.URL_TOO_LONG, page.UrlId, RuleText.Characters(length));
        }
    }

    /// <summary>The URL as people read it: escapes decoded and the host in its own script, so /каталог/ is 9 characters, not 42.</summary>
    private static string DisplayUrl(Uri uri)
    {
        var host = uri.IdnHost.Contains("xn--", StringComparison.Ordinal) ? IDN.GetUnicode(uri.IdnHost) : uri.IdnHost;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{host}{port}{Uri.UnescapeDataString(uri.PathAndQuery)}";
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
