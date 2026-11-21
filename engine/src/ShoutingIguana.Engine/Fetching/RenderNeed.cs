using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Whether a page the HTTP fetch already has should be fetched again in a browser. This reads the raw HTML only to
/// decide; the page is parsed once, afterwards, from whichever HTML is kept.
/// </summary>
internal static class RenderNeed
{
    /// <summary>Visible text shorter than this, with a script and an empty app mount, is a client-rendered shell.</summary>
    public const int SHELL_TEXT_CHARS = 120;

    /// <summary>
    /// Automatic renders a shell. JavaScript renders every HTML page. Matching renders a URL its patterns name.
    /// Off renders nothing.
    /// </summary>
    public static bool ShouldRender(JavaScriptRendering mode, UrlPatterns patterns, string url, string html) => mode switch
    {
        JavaScriptRendering.Off => false,
        JavaScriptRendering.JavaScript => true,
        JavaScriptRendering.Matching => patterns.Matches(url, whenUnsure: false),
        _ => IsClientShell(html)
    };

    /// <summary>
    /// A page a browser has to run before it has content: a script, an empty application mount, and almost no visible
    /// text. A page that already has its text is left as the HTTP response, script tags and all.
    /// </summary>
    public static bool IsClientShell(string html) =>
        html.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
        HasEmptyMount(html) &&
        VisibleTextLength(html) < SHELL_TEXT_CHARS;

    private static readonly string[] MOUNTS = ["id=\"app\"", "id='app'", "id=\"root\"", "id='root'", "id=\"__next\"", "id='__next'", "id=\"__nuxt\"", "id='__nuxt'"];

    private static bool HasEmptyMount(string html)
    {
        foreach (var mount in MOUNTS)
        {
            var at = 0;
            while ((at = html.IndexOf(mount, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (EmptyUntil(html, at, "</div>"))
                {
                    return true;
                }

                at += mount.Length;
            }
        }

        var root = 0;
        while ((root = html.IndexOf("<app-root", root, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (EmptyUntil(html, root, "</app-root>"))
            {
                return true;
            }

            root += "<app-root".Length;
        }

        return false;
    }

    /// <summary>The element at <paramref name="openAt"/> closes at <paramref name="closeTag"/> with only whitespace between.</summary>
    private static bool EmptyUntil(string html, int openAt, string closeTag)
    {
        var openEnd = html.IndexOf('>', openAt);
        if (openEnd < 0)
        {
            return false;
        }

        var close = html.IndexOf(closeTag, openEnd, StringComparison.OrdinalIgnoreCase);
        return close >= 0 && html.AsSpan(openEnd + 1, close - openEnd - 1).IsWhiteSpace();
    }

    /// <summary>Characters of text a reader would see, scripts, styles and noscript left out. Stops once a shell is ruled out.</summary>
    private static int VisibleTextLength(string html)
    {
        var count = 0;
        var index = 0;
        while (index < html.Length && count < SHELL_TEXT_CHARS)
        {
            if (html[index] != '<')
            {
                if (!char.IsWhiteSpace(html[index]))
                {
                    count++;
                }

                index++;
                continue;
            }

            if (html.AsSpan(index).StartsWith("<!--"))
            {
                var commentEnd = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
                index = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }

            var tagEnd = html.IndexOf('>', index + 1);
            if (tagEnd < 0)
            {
                break;
            }

            var name = TagName(html.AsSpan(index + 1, tagEnd - index - 1));
            index = tagEnd + 1;
            if (name is "script" or "style" or "noscript")
            {
                var end = html.IndexOf("</" + name, index, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    break;
                }

                var endTag = html.IndexOf('>', end);
                index = endTag < 0 ? html.Length : endTag + 1;
            }
        }

        return count;
    }

    private static string TagName(ReadOnlySpan<char> tag)
    {
        if (tag.Length == 0 || tag[0] == '/' || tag[0] == '!')
        {
            return "";
        }

        var length = 0;
        while (length < tag.Length && char.IsAsciiLetter(tag[length]))
        {
            length++;
        }

        return tag[..length].ToString().ToLowerInvariant();
    }
}
