using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The faults that checks whose detail is each page's own instance of one group by (<see cref="Models.IssueDefinition.FaultOf"/>):
/// what the detail says, without what differs from page to page, so a template that writes the same mistake on every page
/// is one group however its pages' text differs.
/// </summary>
internal static partial class IssueFaults
{
    /// <summary>HTML's whitespace, which browsers strip from an attribute's URL.</summary>
    private static readonly char[] HTML_WHITESPACE = [' ', '\t', '\n', '\f', '\r'];

    /// <summary>
    /// A JSON parser's message without where it failed, and without the character it failed at when it starts with it: a
    /// missing comma after a product's name fails at a different byte on each product page, and an unescaped quote at a
    /// different letter. "'B' is invalid after a value. Expected either ',', '}', or ']'. LineNumber: 0 | BytePositionInLine: 54."
    /// is "'…' is invalid after a value. Expected either ',', '}', or ']'."
    /// </summary>
    public static string JsonError(string message)
    {
        var fault = Position().Replace(message, string.Empty);
        return LeadingToken().Replace(fault, "'…'");
    }

    /// <summary>Which form of relative URL a canonical's href is: what one template writes on every page, whatever the page.</summary>
    public static string RelativeCanonical(string href)
    {
        var cleaned = href.Trim(HTML_WHITESPACE);
        return cleaned.StartsWith("//", StringComparison.Ordinal) ? "Protocol-relative: starts with //"
            : cleaned.StartsWith('/') ? "Root-relative: starts with /"
            : "Relative to the page's own address";
    }

    /// <summary>What's wrong with a canonical's href that isn't a web address: empty, another scheme, or not a URL at all.</summary>
    public static string InvalidCanonical(string href)
    {
        var cleaned = href.Trim(HTML_WHITESPACE);
        if (cleaned.Length == 0)
        {
            return "Empty";
        }

        return Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute) && absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps
            ? string.Create(CultureInfo.InvariantCulture, $"Uses {absolute.Scheme[..Math.Min(absolute.Scheme.Length, 20)]}: instead of http(s)")
            : "Malformed URL";
    }

    /// <summary>
    /// The language codes a page points at more than one URL, without the URLs: "en-gb: /uk/ · /gb/; de: /de/ · /at/" is
    /// "en-gb, de". A template that lists the same conflicting codes on every page is one group, though each cluster of
    /// pages names its own URLs. A part cut short by the detail's length still gives its code, where it got that far.
    /// </summary>
    /// <summary>
    /// A pagination sequence error's kind, which its detail names with the page's own URLs: "2 rel="next" URLs" is "More than
    /// one rel="next" or rel="prev""; "/list/page/2/ doesn't point back" is "A page doesn't point back"; "rel="next" leads back
    /// to /list/" is "rel="next" loops back"; the page naming itself stays as it is.
    /// </summary>
    public static string PaginationFault(string detail) =>
        detail.EndsWith(" doesn't point back", StringComparison.Ordinal) ? "The next page doesn't point back"
        : detail.StartsWith("rel=\"next\" leads back to ", StringComparison.Ordinal) ? "rel=\"next\" leads back to an earlier page"
        : detail.Contains(" URLs", StringComparison.Ordinal) ? "More than one rel=\"next\" or rel=\"prev\""
        : detail;

    public static string HreflangCodes(string detail) =>
        string.Join(", ", detail.Split("; ").Select(part => (part.IndexOf(": ", StringComparison.Ordinal) is var colon and >= 0 ? part[..colon] : part).TrimEnd('…'))
            .Where(code => code.Length > 0));

    /// <summary>Where System.Text.Json says a document failed: " LineNumber: 0 | BytePositionInLine: 54.", with the JSON path before it if any.</summary>
    [GeneratedRegex(@"\s*(?:Path: .*? \| )?LineNumber: \d+ \| BytePositionInLine: \d+\.?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Position();

    /// <summary>The character (or start of a literal) a message begins by quoting: "'B' is invalid after a value".</summary>
    [GeneratedRegex(@"^'.{1,16}?'(?= (?:is|was) )", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingToken();
}
