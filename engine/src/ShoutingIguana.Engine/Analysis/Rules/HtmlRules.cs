using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Document basics on every page, indexable or not: the head's tags where they belong, the document's structure, viewport,
/// language and encoding, and a separate mobile version. Open Graph tags on indexable pages only.
/// </summary>
internal sealed class HtmlRules : IPageRule
{
    /// <summary>The narrowest most-zoom that lets text be enlarged to 200%, as WCAG asks (axe's meta-viewport rule).</summary>
    private const double MIN_MAXIMUM_SCALE = 2;

    /// <summary>The WHATWG labels of UTF-8.</summary>
    private static readonly HashSet<string> UTF8 = new(["utf-8", "utf8", "unicode-1-1-utf-8"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] OPEN_GRAPH = ["og:title", "og:description", "og:image"];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var facts = page.Facts;
        if (!HasMeta(facts.MetaTags, "viewport"))
        {
            yield return new IssueHit(IssueCatalog.VIEWPORT_MISSING, page.UrlId);
        }

        if (string.IsNullOrWhiteSpace(facts.Language))
        {
            yield return new IssueHit(IssueCatalog.LANG_MISSING, page.UrlId);
        }

        // A byte-order mark declares the encoding too, and beats the other two. A header with an empty charset declares none,
        // and browsers look for the meta tag in the first 1024 bytes only.
        var lateMeta = facts.DeclaredCharsetEnd is > CharsetDecoder.PRESCAN_BYTES;
        if ((facts.DeclaredCharset is null || lateMeta) && facts.ByteOrderMark is null && ContentTypeHeader.CharsetOf(page.Header("content-type")) is null)
        {
            yield return new IssueHit(IssueCatalog.CHARSET_MISSING, page.UrlId,
                lateMeta ? $"A meta tag declares it, but ends at byte {facts.DeclaredCharsetEnd:N0}, past the first {CharsetDecoder.PRESCAN_BYTES:N0} bytes browsers read" : null);
        }

        if (facts.HeadTagsInBody.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HEAD_TAGS_IN_BODY, page.UrlId, HeadTagsText(facts.HeadTagsInBody, facts.HeadEnd));
        }

        if (facts.StructureErrors.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HTML_STRUCTURE_ERRORS, page.UrlId, string.Join("; ", facts.StructureErrors));
        }

        if (ViewportFaults(facts.MetaTags) is { Count: > 0 } viewport)
        {
            yield return new IssueHit(IssueCatalog.VIEWPORT_INVALID, page.UrlId, RuleText.Fit(string.Join("; ", viewport)));
        }

        if (CharsetFault(facts, page.Header("content-type")) is { } charset)
        {
            yield return new IssueHit(IssueCatalog.CHARSET_INVALID, page.UrlId, charset);
        }

        if (!string.IsNullOrWhiteSpace(facts.Language) && !IsLanguageTag(facts.Language))
        {
            yield return new IssueHit(IssueCatalog.LANG_INVALID, page.UrlId, RuleText.Cut(facts.Language, 60));
        }

        if (facts.MobileAlternate is { } mobile && mobile != page.Url)
        {
            yield return new IssueHit(IssueCatalog.MOBILE_ALTERNATE, page.UrlId, RuleText.Fit($"Mobile version: {mobile}"));
        }

        if (page.IsIndexable && OPEN_GRAPH.Where(name => !HasMeta(facts.MetaTags, name)).ToList() is { Count: > 0 } lacking)
        {
            // A tag that's there but empty does no more than a missing one, but the fix is different, so say which.
            var empty = lacking.Where(name => facts.MetaTags.Any(tag => tag.Name == name)).ToList();
            var missing = lacking.Except(empty).ToList();
            var detail = (missing.Count > 0, empty.Count > 0) switch
            {
                (true, true) => $"Missing {string.Join(", ", missing)}; empty {string.Join(", ", empty)}",
                (true, false) => $"Missing {string.Join(", ", missing)}",
                _ => $"Empty {string.Join(", ", empty)}"
            };

            yield return new IssueHit(IssueCatalog.OPEN_GRAPH_MISSING, page.UrlId, detail);
        }
    }

    /// <summary>
    /// "title, meta description and 3 Open Graph tags are in the body: &lt;div id="promo"&gt; on line 12 ended the head early",
    /// or "… are in the body, after &lt;body&gt; on line 30" for tags written in the body.
    /// </summary>
    private static string HeadTagsText(IReadOnlyList<string> tags, HeadEndFact? headEnd)
    {
        var openGraph = tags.Count(tag => tag.StartsWith("og:", StringComparison.Ordinal));
        var x = tags.Count(tag => tag.StartsWith("twitter:", StringComparison.Ordinal));
        List<string> named = [.. tags.Where(tag => !tag.StartsWith("og:", StringComparison.Ordinal) && !tag.StartsWith("twitter:", StringComparison.Ordinal)).Distinct()];
        if (openGraph > 0)
        {
            named.Add(RuleText.Count(openGraph, "Open Graph tag", "Open Graph tags"));
        }

        if (x > 0)
        {
            named.Add(RuleText.Count(x, "X tag", "X tags"));
        }

        var what = named.Count == 1 ? named[0] : $"{string.Join(", ", named[..^1])} and {named[^1]}";
        var verb = tags.Count == 1 ? "is" : "are";
        var cause = headEnd switch
        {
            null => string.Empty,
            { Implicit: false, Line: { } line } => string.Create(CultureInfo.InvariantCulture, $", after <body> on line {line:N0}"),
            { Implicit: false } => ", after <body>",
            { Line: { } at } => string.Create(CultureInfo.InvariantCulture, $": {headEnd.Element} on line {at:N0} ended the head early"),
            _ => $": {char.ToUpperInvariant(headEnd.Element[0])}{headEnd.Element[1..]} ended the head early"
        };
        return RuleText.Fit($"{what} {verb} in the body{cause}");
    }

    /// <summary>What's wrong with the page's viewport meta tags: zoom stopped or limited, no width, two that differ. Empty with none, or no viewport.</summary>
    private static List<string> ViewportFaults(IReadOnlyList<MetaTagFact> tags)
    {
        var viewports = tags.Where(tag => tag.Name == "viewport" && !string.IsNullOrWhiteSpace(tag.Content)).Select(tag => tag.Content).ToList();
        if (viewports.Count == 0)
        {
            return [];
        }

        var settings = viewports[0].Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(setting => setting.Split('=', 2) is [var key, var value] ? (Key: key.Trim().ToLowerInvariant(), Value: value.Trim().ToLowerInvariant()) : (Key: setting.ToLowerInvariant(), Value: string.Empty))
            .ToList();
        List<string> faults = [];
        if (settings.FirstOrDefault(setting => setting.Key == "user-scalable").Value is var scalable && scalable is "no" or "0")
        {
            faults.Add($"user-scalable={scalable}");
        }

        if (settings.FirstOrDefault(setting => setting.Key == "maximum-scale").Value is { Length: > 0 } maximum &&
            double.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale < MIN_MAXIMUM_SCALE)
        {
            faults.Add($"maximum-scale={maximum}");
        }

        if (!settings.Any(setting => setting.Key is "width" or "initial-scale"))
        {
            faults.Add("no width or initial-scale");
        }

        if (viewports.Select(Compact).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            faults.Add(RuleText.Count(viewports.Count, "viewport tag", "viewport tags") + " that differ");
        }

        return faults;

        static string Compact(string content) => string.Concat(content.Where(character => !char.IsWhiteSpace(character)));
    }

    /// <summary>
    /// What's wrong with how the page declares its encoding, the first of: the header and the meta tag name different
    /// encodings; the one that counts (a byte-order mark, else the header, else the meta tag) isn't UTF-8; two meta
    /// declarations differ; the meta tag is past the first 1024 bytes though another declaration exists (with none, the
    /// encoding is missing, its own check). Null when nothing is.
    /// </summary>
    private static string? CharsetFault(PageFacts facts, string? contentType)
    {
        var header = ContentTypeHeader.CharsetOf(contentType);
        var lateMeta = facts.DeclaredCharsetEnd is > CharsetDecoder.PRESCAN_BYTES;
        var meta = lateMeta ? null : facts.DeclaredCharset;
        if (facts.ByteOrderMark is null && header is not null && meta is not null && !SameEncoding(header, meta))
        {
            return RuleText.Fit($"The header says {header}; the page says {meta}");
        }

        if ((facts.ByteOrderMark ?? header ?? meta) is { } declared && !UTF8.Contains(declared.Trim()))
        {
            return RuleText.Fit($"Declares {declared.Trim()}, not UTF-8");
        }

        if (facts.CharsetDeclarations.Select(Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return RuleText.Fit($"{RuleText.Count(facts.CharsetDeclarations.Count, "declaration", "declarations")}: {string.Join(", ", facts.CharsetDeclarations)}");
        }

        return lateMeta && (facts.ByteOrderMark is not null || header is not null)
            ? string.Create(CultureInfo.InvariantCulture, $"<meta charset> after the first {CharsetDecoder.PRESCAN_BYTES:N0} bytes")
            : null;

        static string Label(string charset) => UTF8.Contains(charset.Trim()) ? "utf-8" : charset.Trim();
        static bool SameEncoding(string one, string other) => string.Equals(Label(one), Label(other), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A well-formed BCP 47 language tag, as HTML's lang takes: a 2-letter ISO 639-1 language or a 3-letter one, an optional
    /// 4-letter script, an optional ISO 3166-1 region or 3-digit area, then variants (5 to 8 characters, or 4 from a digit)
    /// and extensions or private use (a single character and the subtags after it), all separated by hyphens.
    /// </summary>
    internal static bool IsLanguageTag(string value)
    {
        var parts = value.Trim().Split('-');
        if (parts.Any(part => part.Length is 0 or > 8 || !part.All(char.IsAsciiLetterOrDigit)))
        {
            return false;
        }

        var primary = parts[0];
        if (!(primary.Length == 2 && HreflangCodes.IsLanguage(primary)) && !(primary.Length == 3 && primary.All(char.IsAsciiLetter)))
        {
            return false;
        }

        var next = 1;
        if (next < parts.Length && parts[next].Length == 4 && parts[next].All(char.IsAsciiLetter))
        {
            next++;
        }

        if (next < parts.Length && ((parts[next].Length == 2 && HreflangCodes.IsRegion(parts[next])) || (parts[next].Length == 3 && parts[next].All(char.IsAsciiDigit))))
        {
            next++;
        }

        for (; next < parts.Length; next++)
        {
            var part = parts[next];
            if (part.Length == 1)
            {
                // An extension or private use: the subtags after it are its own.
                return next < parts.Length - 1;
            }

            if (part.Length < 5 && !(part.Length == 4 && char.IsAsciiDigit(part[0])))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMeta(IReadOnlyList<MetaTagFact> tags, string name) =>
        tags.Any(tag => tag.Name == name && !string.IsNullOrWhiteSpace(tag.Content));
}
