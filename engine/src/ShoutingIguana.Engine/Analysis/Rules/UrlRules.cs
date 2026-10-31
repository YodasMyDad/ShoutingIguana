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
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{UnicodeHost(uri.IdnHost)}{port}{Uri.UnescapeDataString(uri.PathAndQuery)}";
    }

    /// <summary>
    /// The host in Unicode, or as written when a label isn't valid punycode: DNS accepts any xn-- label, IDNA rejects
    /// some, and IdnMapping has no non-throwing way to tell.
    /// </summary>
    private static string UnicodeHost(string host)
    {
        if (!host.Contains("xn--", StringComparison.Ordinal))
        {
            return host;
        }

        try
        {
            return IDN.GetUnicode(host);
        }
        catch (ArgumentException)
        {
            return host;
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
