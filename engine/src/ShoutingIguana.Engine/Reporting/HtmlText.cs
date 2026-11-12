using System.Text;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Text on its way into the report's HTML. Everything a crawl holds is the site's (a URL, a detail, an anchor), and the
/// report is opened in a browser and mailed on, so every string is escaped where it's written, as text and inside an
/// attribute's quotes alike: nothing a site sends can become markup. Characters that have no place in a line of a
/// document go: control characters, and the ones that reorder the text around them, by which a URL can be made to read
/// as another.
/// </summary>
internal static class HtmlText
{
    /// <summary>Appends text, escaped for an element's content and for a quoted attribute's value alike.</summary>
    public static StringBuilder AppendText(this StringBuilder html, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    html.Append("&amp;");
                    break;
                case '<':
                    html.Append("&lt;");
                    break;
                case '>':
                    html.Append("&gt;");
                    break;
                case '"':
                    html.Append("&quot;");
                    break;
                case '\'':
                    html.Append("&#39;");
                    break;
                case < ' ' or (>= '\u007F' and <= '\u009F'):
                    // A tab or a line break is a space; no other control character has a place in a document.
                    html.Append(' ');
                    break;
                case (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\uFEFF':
                    // Marks that change the direction of the text after them: left out, so a URL reads in the order it's stored.
                    break;
                default:
                    html.Append(character);
                    break;
            }
        }

        return html;
    }

    /// <summary>Text cut to a length, ending "…" where it was cut, never between the two halves of a surrogate pair.</summary>
    public static string Cut(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var end = char.IsHighSurrogate(text[length - 2]) ? length - 2 : length - 1;
        return string.Concat(text.AsSpan(0, end), "…");
    }

    /// <summary>
    /// The URL when a link may lead to it: an absolute http or https URL, of a length a browser takes. Anything else (a
    /// javascript: or data: URL a site wrote in a canonical, text that isn't a URL) is shown as text and never linked.
    /// </summary>
    public static string? Linkable(string url) =>
        url.Length <= MAX_HREF && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) && !url.AsSpan().ContainsAny(UNLINKABLE)
            ? url
            : null;

    private const int MAX_HREF = 2_000;

    /// <summary>What a URL written as text loses (control characters, and the marks that reorder text): a URL that holds one isn't linked, as the link wouldn't be the URL.</summary>
    private static readonly System.Buffers.SearchValues<char> UNLINKABLE = System.Buffers.SearchValues.Create(
        string.Concat(Enumerable.Range(0, 0x20).Concat(Enumerable.Range(0x7F, 0x21)).Select(code => (char)code)) + "\u202A\u202B\u202C\u202D\u202E\u2066\u2067\u2068\u2069\u200E\u200F\uFEFF");
}
