using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Whether a page the HTTP fetch already has should be fetched again in a browser. This reads the raw HTML only to
/// decide; the page is parsed once, afterwards, from whichever HTML is kept.
/// </summary>
internal static class RenderNeed
{
    /// <summary>Visible text shorter than this, on a page with a script, is a client-rendered shell.</summary>
    public const int SHELL_TEXT_CHARS = 120;

    /// <summary>
    /// A page with less visible text than this, whose application mount holds next to none of it, is a shell too: a
    /// menu and a footer round a spinner. A page with more has its text, and an empty mount on it is a widget.
    /// </summary>
    public const int SKELETON_PAGE_CHARS = 1_000;

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
    /// A page a browser has to run before it has content. It has a script, and either almost no visible text at all,
    /// or little text and an application mount (<c>#app</c>, <c>#root</c>, <c>#__next</c>, <c>#__nuxt</c>,
    /// <c>app-root</c>) with almost none inside it. A page that already has its text is left as the HTTP response,
    /// script tags and all.
    /// </summary>
    public static bool IsClientShell(string html)
    {
        if (!html.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = VisibleTextLength(html, 0, html.Length, SKELETON_PAGE_CHARS);
        return text < SHELL_TEXT_CHARS || (text < SKELETON_PAGE_CHARS && HasBareMount(html));
    }

    private static readonly string[] MOUNTS = ["id=\"app\"", "id='app'", "id=\"root\"", "id='root'", "id=\"__next\"", "id='__next'", "id=\"__nuxt\"", "id='__nuxt'"];

    /// <summary>An application mount with under <see cref="SHELL_TEXT_CHARS"/> of visible text inside it.</summary>
    private static bool HasBareMount(string html)
    {
        foreach (var mount in MOUNTS)
        {
            var at = 0;
            while ((at = html.IndexOf(mount, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var open = html.LastIndexOf('<', at);
                if (open >= 0 && IsBare(html, open))
                {
                    return true;
                }

                at += mount.Length;
            }
        }

        var root = 0;
        while ((root = html.IndexOf("<app-root", root, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (IsBare(html, root))
            {
                return true;
            }

            root += "<app-root".Length;
        }

        return false;
    }

    /// <summary>The element whose tag opens at <paramref name="open"/> has almost no visible text before it closes.</summary>
    private static bool IsBare(string html, int open)
    {
        var openEnd = html.IndexOf('>', open);
        if (openEnd < 0)
        {
            return false;
        }

        var name = TagName(html.AsSpan(open + 1, openEnd - open - 1), allowHyphen: true);
        if (name.Length == 0)
        {
            return false;
        }

        var close = ClosingTagOf(html, openEnd + 1, name);
        return VisibleTextLength(html, openEnd + 1, close, SHELL_TEXT_CHARS) < SHELL_TEXT_CHARS;
    }

    /// <summary>Where the element opened before <paramref name="from"/> closes, counting the same element nested in it.</summary>
    private static int ClosingTagOf(string html, int from, string name)
    {
        var depth = 1;
        var index = from;
        while ((index = html.IndexOf('<', index)) >= 0)
        {
            var isClose = index + 1 < html.Length && html[index + 1] == '/';
            var nameAt = index + (isClose ? 2 : 1);
            if (html.AsSpan(nameAt).StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                (nameAt + name.Length >= html.Length || !IsNameChar(html[nameAt + name.Length])))
            {
                depth += isClose ? -1 : 1;
                if (depth == 0)
                {
                    return index;
                }
            }

            index++;
        }

        return html.Length;
    }

    private static bool IsNameChar(char character) => char.IsAsciiLetterOrDigit(character) || character == '-';

    /// <summary>
    /// Characters of text a reader would see between two places in the HTML, scripts, styles and noscript left out.
    /// Stops at <paramref name="enough"/>.
    /// </summary>
    private static int VisibleTextLength(string html, int from, int to, int enough)
    {
        var count = 0;
        var index = from;
        while (index < to && count < enough)
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

            var name = TagName(html.AsSpan(index + 1, tagEnd - index - 1), allowHyphen: false);
            index = tagEnd + 1;
            if (name is "script" or "style" or "noscript" or "title" or "template")
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

    private static string TagName(ReadOnlySpan<char> tag, bool allowHyphen)
    {
        if (tag.Length == 0 || tag[0] == '/' || tag[0] == '!')
        {
            return "";
        }

        var length = 0;
        while (length < tag.Length && (char.IsAsciiLetterOrDigit(tag[length]) || (allowHyphen && tag[length] == '-')))
        {
            length++;
        }

        return tag[..length].ToString().ToLowerInvariant();
    }
}
