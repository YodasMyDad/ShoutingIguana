using AngleSharp.Html.Parser;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Parses a page once with a spec-compliant HTML5 parser and returns its facts and links. The tree is the
/// one a browser builds, so a canonical pushed into the body by a stray element in the head is reported
/// as outside the head, exactly as Google sees it. Parsing runs with scripting on, as in browsers and Google's
/// renderer: noscript content is then text, so a tracking pixel's &lt;noscript&gt;&lt;img&gt; in the head doesn't end it.
/// </summary>
public static class HtmlPageParser
{
    private static readonly ThreadLocal<HtmlParser> PARSER = new(() => new HtmlParser(new HtmlParserOptions { IsScripting = true }));

    /// <param name="byteOrderMark">
    /// The encoding a byte-order mark at the start of the response's bytes declared (<see cref="PageFacts.ByteOrderMark"/>),
    /// or null with none. The fetch sees it, and the decoded <paramref name="html"/> no longer has it.
    /// </param>
    public static ParsedPage Parse(string html, Uri pageUri, IReadOnlyList<ResponseHeader> headers, string robotsToken, string? byteOrderMark = null)
    {
        using var document = PARSER.Value!.ParseDocument(html);

        // Browsers use the first base with an href anywhere in the document, even one a stray element pushed into the body.
        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        var baseUri = baseHref is not null && UrlNormalizer.TryResolve(pageUri, baseHref, out var resolvedBase)
            ? resolvedBase
            : pageUri;

        using var text = new TextAccumulator();
        var walker = new PageWalker(pageUri, baseUri, robotsToken, text);
        if (document.DocumentElement is { } root)
        {
            walker.Walk(root);
        }

        var signals = ReadHeaderSignals(headers, pageUri);
        var fingerprint = ContentFingerprint.Compute(text.AsSpan());
        var facts = new PageFacts
        {
            Titles = walker.Titles,
            MetaDescriptions = walker.MetaDescriptions,
            MetaTags = walker.MetaTags,
            Canonicals = [.. walker.Canonicals, .. signals.Canonicals],
            Hreflangs = [.. walker.Hreflangs, .. signals.Hreflangs],
            Headings = walker.Headings,
            Images = walker.Images,
            JsonLd = walker.JsonLd,
            AnchorIds = walker.AnchorIds,
            MetaRefresh = walker.MetaRefresh,
            Robots = RobotsMetaParser.Parse(walker.RobotsMetaContents, signals.RobotsTagValues, robotsToken),
            Language = walker.Language,
            DeclaredCharset = walker.DeclaredCharset,
            ByteOrderMark = byteOrderMark,
            BaseHref = baseHref,
            WordCount = fingerprint.WordCount,
            ContentHash = fingerprint.ContentHash,
            SimHash = fingerprint.SimHash,
            SimHashLanes = fingerprint.Lanes
        };

        return new ParsedPage(facts, [.. walker.Links, .. signals.Links]);
    }

    /// <summary>Facts for a non-HTML response: robots directives and canonicals carried in its headers.</summary>
    public static ParsedPage ParseHeaders(Uri uri, IReadOnlyList<ResponseHeader> headers, string robotsToken)
    {
        var signals = ReadHeaderSignals(headers, uri);
        var facts = new PageFacts
        {
            Canonicals = signals.Canonicals,
            Hreflangs = signals.Hreflangs,
            Robots = RobotsMetaParser.Parse([], signals.RobotsTagValues, robotsToken)
        };

        return new ParsedPage(facts, signals.Links);
    }

    private static HeaderSignals ReadHeaderSignals(IReadOnlyList<ResponseHeader> headers, Uri pageUri)
    {
        List<string> robotsTagValues = [];
        List<string> linkValues = [];
        foreach (var header in headers)
        {
            if (header.Name == "x-robots-tag")
            {
                robotsTagValues.Add(header.Value);
            }
            else if (header.Name == "link")
            {
                linkValues.Add(header.Value);
            }
        }

        if (linkValues.Count == 0)
        {
            return new HeaderSignals([], [], [], robotsTagValues);
        }

        List<CanonicalFact> canonicals = [];
        List<HreflangFact> hreflangs = [];
        List<ExtractedLink> links = [];
        foreach (var entry in LinkHeaderParser.Parse(linkValues))
        {
            var rels = entry.Rel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolved = UrlNormalizer.TryResolve(pageUri, entry.Url, out var target);
            var normalized = resolved ? UrlNormalizer.Normalize(target) : null;

            if (rels.Contains("canonical"))
            {
                canonicals.Add(new CanonicalFact(entry.Url, normalized, InHead: false, FromHttpHeader: true));
                if (resolved)
                {
                    links.Add(new ExtractedLink(target, LinkKind.Canonical, LinkFlags.FromHttpHeader, LinkPosition.Head, null));
                }
            }

            if (rels.Contains("alternate") && entry.Hreflang is { Length: > 0 } language)
            {
                hreflangs.Add(new HreflangFact(language, entry.Url, normalized, InHead: false, FromHttpHeader: true));
                if (resolved)
                {
                    links.Add(new ExtractedLink(target, LinkKind.Hreflang, LinkFlags.FromHttpHeader, LinkPosition.Head, language));
                }
            }
        }

        return new HeaderSignals(canonicals, hreflangs, links, robotsTagValues);
    }
}
