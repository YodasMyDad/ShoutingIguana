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
    /// A JSON parser's message without where it failed, and without the letter or digit it failed at when it starts with
    /// one: a missing comma after a product's name fails at a different place on each product page, and an unescaped quote
    /// at a different letter. "Block 2 of 3, line 1, position 55: 'B' is invalid after a value. Expected either ',', '}', or
    /// ']'." is "'…' is invalid after a value. Expected either ',', '}', or ']'."; a quote or bracket it failed at, the same
    /// on every page of a template, is kept. An earlier engine's detail, the parser's own message ending "LineNumber: 0 |
    /// BytePositionInLine: 54.", is read the same.
    /// </summary>
    public static string JsonError(string message)
    {
        var fault = WithoutPosition(JsonBlock().Replace(message, string.Empty));
        return LeadingToken().Replace(fault, token => token.Value[1..^1].Any(char.IsLetterOrDigit) ? "'…'" : token.Value);
    }

    /// <summary>System.Text.Json's message without where it failed: "… LineNumber: 0 | BytePositionInLine: 54." without its last part.</summary>
    public static string WithoutPosition(string message) => Position().Replace(message, string.Empty);

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

    /// <summary>
    /// What put a page's head tags in its body, which a head-tags-in-body detail ends with: "&lt;div id="promo"&gt; ended the
    /// head early" (the line, which differs from page to page, left out), or "after &lt;body&gt;" for tags written in the body.
    /// </summary>
    public static string HeadEndCause(string detail)
    {
        var after = detail.LastIndexOf(" in the body, after <body>", StringComparison.Ordinal);
        var colon = detail.LastIndexOf(" in the body: ", StringComparison.Ordinal);
        if (after >= 0 && after > colon)
        {
            return "After <body>";
        }

        if (colon < 0)
        {
            return detail;
        }

        var cause = detail[(colon + " in the body: ".Length)..];
        var line = cause.LastIndexOf(" on line ", StringComparison.Ordinal);
        return line > 0 ? cause[..line] + " ended the head early" : cause;
    }

    /// <summary>
    /// A skipped heading level's step, without the heading it quotes: "H1 → H4: "Out Of Stock"" is "H1 → H4", which a
    /// template that skips the same level on every page writes whatever its headings say.
    /// </summary>
    public static string HeadingStep(string detail)
    {
        var quote = detail.IndexOf(": \"", StringComparison.Ordinal);
        return quote < 0 ? detail : detail[..quote];
    }

    /// <summary>
    /// The share tags a page gives more than once, counted, without their values: "2 og:title: "Sofas" · "Chairs"; 2
    /// twitter:card: …" is "2 og:title, 2 twitter:card", as an earlier engine's detail was. Two plugins writing the same
    /// tags on every page write different values on each.
    /// </summary>
    public static string RepeatedTags(string detail) =>
        string.Join(", ", RepeatedTag().Matches(detail).Select(match => match.Groups["tag"].Value));

    public static string HreflangCodes(string detail) =>
        string.Join(", ", detail.Split("; ").Select(part => (part.IndexOf(": ", StringComparison.Ordinal) is var colon and >= 0 ? part[..colon] : part).TrimEnd('…'))
            .Where(code => code.Length > 0));

    /// <summary>
    /// A document structure error's faults, without the line each is on: "2 &lt;head&gt; tags (the second on line 14); 2
    /// &lt;body&gt; tags (the second on line 40)" is "2 &lt;head&gt; tags; 2 &lt;body&gt; tags", as an earlier engine's detail was.
    /// </summary>
    public static string StructureFaults(string detail) => StructureLine().Replace(detail, string.Empty);

    /// <summary>A structure error's line: " (on line 2)", " (the second on line 1,204)".</summary>
    [GeneratedRegex(@" \((?:the second )?on line [0-9][0-9,]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex StructureLine();

    /// <summary>A share tag counted at the start of a social-tags-multiple detail's part: "2 og:title" before ": "", ", " or the end.</summary>
    [GeneratedRegex("""(?:^|; |, )(?<tag>[0-9][0-9,]* (?:og|twitter):[a-z_:]+)(?=: "|, |$)""", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedTag();

    /// <summary>Which JSON-LD block of a page failed, and where in it: "Block 2 of 3, line 1, position 55: ".</summary>
    [GeneratedRegex(@"^Block [\d,]+ of [\d,]+(?:, line [\d,]+, position [\d,]+)?: ", RegexOptions.CultureInvariant)]
    private static partial Regex JsonBlock();

    /// <summary>Where System.Text.Json says a document failed: " LineNumber: 0 | BytePositionInLine: 54.", with the JSON path before it if any.</summary>
    [GeneratedRegex(@"\s*(?:Path: .*? \| )?LineNumber: \d+ \| BytePositionInLine: \d+\.?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Position();

    /// <summary>The character (or start of a literal) a message begins by quoting: "'B' is invalid after a value".</summary>
    [GeneratedRegex(@"^'.{1,16}?'(?= (?:is|was) )", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingToken();
}
