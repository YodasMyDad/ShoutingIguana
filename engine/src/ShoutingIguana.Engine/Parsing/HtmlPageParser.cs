using AngleSharp.Dom;
using AngleSharp.Html.Dom.Events;
using AngleSharp.Html.Parser;
using AngleSharp.Text;
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
    /// <summary>
    /// A parser a thread, with what notes where its documents' bodies begin (<see cref="HeadEndRecorder"/>). As it creates
    /// each element it also notes where each meta element starts (<see cref="RecordMeta"/>).
    /// </summary>
    private static readonly ThreadLocal<(HtmlParser Parser, HeadEndRecorder HeadEnd)> PARSER = new(() =>
    {
        var headEnd = new HeadEndRecorder();
        var options = new HtmlParserOptions
        {
            IsScripting = true,
            OnCreated = (element, position) =>
            {
                headEnd.Created(element, position);
                RecordMeta(element, position);
            }
        };
        var parser = new HtmlParser(options);
        parser.Error += (_, error) => RecordError(error as HtmlErrorEvent);
        return (parser, headEnd);
    });

    /// <summary>
    /// The document-structure errors the parser met in the page being parsed on this thread, with where each tag starts in
    /// the text (from 0). Emptied before each parse.
    /// </summary>
    [ThreadStatic]
    private static List<(HtmlParseError Code, int Start)>? _structureErrors;

    /// <summary>
    /// Where each meta element of the page being parsed on this thread starts in its text, as the parser creates them: how
    /// far in a charset declaration is, without keeping a source position on every element. Emptied after each parse.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<IElement, int>? _metaStarts;

    /// <param name="byteOrderMark">
    /// The encoding a byte-order mark at the start of the response's bytes declared (<see cref="PageFacts.ByteOrderMark"/>),
    /// or null with none. The fetch sees it, and the decoded <paramref name="html"/> no longer has it.
    /// </param>
    public static ParsedPage Parse(string html, Uri pageUri, IReadOnlyList<ResponseHeader> headers, string robotsToken, string? byteOrderMark = null)
    {
        var metaStarts = _metaStarts ??= new Dictionary<IElement, int>(ReferenceEqualityComparer.Instance);
        try
        {
            return Parse(html, pageUri, headers, robotsToken, byteOrderMark, metaStarts);
        }
        finally
        {
            // The elements hold the whole document, which mustn't outlive the parse.
            metaStarts.Clear();
        }
    }

    private static ParsedPage Parse(string html, Uri pageUri, IReadOnlyList<ResponseHeader> headers, string robotsToken, string? byteOrderMark,
        Dictionary<IElement, int> metaStarts)
    {
        var (parser, headEnd) = PARSER.Value;
        headEnd.Reset();
        var structureErrors = _structureErrors ??= [];
        structureErrors.Clear();
        using var document = parser.ParseDocument(html);

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
        var (sentences, syllables) = TextMeasures.SentencesAndSyllables(text.AsSpan());
        var (refresh, refreshLink) = RefreshOf(headers, baseUri, walker.MetaRefresh);
        var facts = new PageFacts
        {
            CollectedVersion = PageFacts.CURRENT_COLLECTION,
            Titles = walker.Titles,
            MetaDescriptions = walker.MetaDescriptions,
            MetaTags = walker.MetaTags,
            Canonicals = [.. walker.Canonicals, .. signals.Canonicals],
            Hreflangs = [.. walker.Hreflangs, .. signals.Hreflangs],
            Pagination = walker.Pagination,
            UncrawlableLinks = walker.UncrawlableCount > 0 ? new UncrawlableLinksFact(walker.UncrawlableCount, walker.UncrawlableExamples) : null,
            Headings = walker.Headings,
            Images = walker.Images,
            JsonLd = walker.JsonLd,
            Microdata = MicrodataReader.Read(document, baseUri),
            Forms = walker.Forms,
            ProtocolRelativeUrls = walker.FirstProtocolRelative is { } first
                ? new ProtocolRelativeFact(walker.ProtocolRelativeCount, first)
                {
                    Unlinked = walker.ProtocolRelativeUnlinked, FirstUnlinked = walker.FirstUnlinkedProtocolRelative
                }
                : null,
            PlaceholderText = TextMeasures.Placeholder(text.AsSpan()),
            SentenceCount = sentences,
            SyllableCount = syllables,
            TotalWordCount = fingerprint.WordCount + walker.TemplateWordCount,
            AnchorIds = walker.AnchorIds,
            Icons = walker.Icons,
            ShopifyMarkup = walker.ShopifyMarkup,
            MetaRefresh = refresh,
            Robots = RobotsMetaParser.Parse(walker.RobotsMetaContents, signals.RobotsTagValues, robotsToken),
            Language = walker.Language,
            DeclaredCharset = walker.DeclaredCharset,
            DeclaredCharsetEnd = walker.CharsetElement is { } declaration && metaStarts.TryGetValue(declaration, out var start)
                ? EndOfTag(html, start, walker.DeclaredCharset)
                : null,
            ByteOrderMark = byteOrderMark,
            BaseHref = baseHref,
            HeadEnd = walker.Canonicals.Any(canonical => !canonical.InHead) || walker.Hreflangs.Any(hreflang => !hreflang.InHead) || walker.HeadTagsInBody.Count > 0
                ? headEnd.HeadEnd(document)
                : null,
            HeadTagsInBody = walker.HeadTagsInBody,
            StructureErrors = StructureErrors(structureErrors, html, headEnd.HeadImplied, headEnd.BodyImplied),
            CharsetDeclarations = walker.CharsetDeclarations,
            MobileAlternate = walker.MobileAlternate,
            WordCount = fingerprint.WordCount,
            ContentHash = fingerprint.ContentHash,
            SimHash = fingerprint.SimHash,
            SimHashLanes = fingerprint.Lanes
        };

        // A refresh the header made the page's leaves the meta tag's link out: it never runs.
        IEnumerable<ExtractedLink> links = refresh is { FromHttpHeader: true } ? walker.Links.Where(link => link.Kind != LinkKind.MetaRefresh) : walker.Links;
        return new ParsedPage(facts, [.. links, .. signals.Links, .. refreshLink]);
    }

    /// <summary>
    /// The page's refresh: a <c>Refresh</c> response header's, which browsers act on as they create the document and so
    /// before any meta tag (a document refreshes once, HTML's "shared declarative refresh steps"), else the meta tag's. The
    /// header's URL is a link of the page's, as the meta tag's is, so an instant one makes the page a redirect.
    /// </summary>
    private static (MetaRefreshFact? Refresh, ExtractedLink[] Link) RefreshOf(IReadOnlyList<ResponseHeader> headers, Uri baseUri, MetaRefreshFact? meta)
    {
        if (headers.FirstOrDefault(header => header.Name == "refresh").Value is not { } value || string.IsNullOrWhiteSpace(value))
        {
            return (meta, []);
        }

        var refresh = PageWalker.ParseRefresh(value) with { FromHttpHeader = true };
        return refresh.Url is { } url && UrlNormalizer.TryResolve(baseUri, url, out var target)
            ? (refresh, [new ExtractedLink(target, LinkKind.MetaRefresh, LinkFlags.FromHttpHeader, LinkPosition.Head, null)])
            : (refresh, []);
    }

    /// <summary>
    /// Notes the errors that say the document's structure is wrong. A &lt;head&gt; tag in the body is reported only as a tag
    /// that can't start there, so it's told by its text.
    /// </summary>
    private static void RecordError(HtmlErrorEvent? error)
    {
        if (error is not null && _structureErrors is { } errors && (HtmlParseError)error.Code is HtmlParseError.HeadTagMisplaced or
                HtmlParseError.BodyTagMisplaced or HtmlParseError.HtmlTagMisplaced or HtmlParseError.TagCannotStartHere)
        {
            errors.Add(((HtmlParseError)error.Code, error.Position.Position - 1));
        }
    }

    /// <summary>The text at <paramref name="start"/> is a &lt;head&gt; start tag.</summary>
    private static bool IsHeadTag(string html, int start) =>
        start >= 0 && start + 5 < html.Length && html.AsSpan(start, 5).Equals("<head", StringComparison.OrdinalIgnoreCase) &&
        (char.IsWhiteSpace(html[start + 5]) || html[start + 5] is '>' or '/');

    /// <summary>
    /// The document's structure errors, described: a second &lt;head&gt; (or one after content), a second &lt;body&gt;, an
    /// &lt;html&gt; after content. A body the parser made up (for a stray element in the head) and then the page's own &lt;body&gt;
    /// tag are one body, whose stray element the head's checks report.
    /// </summary>
    private static List<string> StructureErrors(List<(HtmlParseError Code, int Start)> errors, string html, bool headImplied, bool bodyImplied)
    {
        var heads = errors.Count(error => error.Code == HtmlParseError.HeadTagMisplaced ||
                                          (error.Code == HtmlParseError.TagCannotStartHere && IsHeadTag(html, error.Start)));
        var bodies = errors.Count(error => error.Code == HtmlParseError.BodyTagMisplaced) - (bodyImplied ? 1 : 0);
        List<string> faults = [];
        if (errors.Any(error => error.Code == HtmlParseError.HtmlTagMisplaced))
        {
            faults.Add("<html> after content");
        }

        // A head the parser made up for content before it, then the page's own <head> tag: one head, in the wrong place.
        if (heads > 0)
        {
            faults.Add(headImplied && heads == 1 ? "<head> after content" : $"{heads + (headImplied ? 0 : 1)} <head> tags");
        }

        if (bodies > 0)
        {
            faults.Add($"{bodies + 1} <body> tags");
        }

        return faults;
    }

    /// <summary>The parser reports a new element's position one-based, at its opening '&lt;'.</summary>
    private static void RecordMeta(IElement element, TextPosition position)
    {
        if (element.LocalName == "meta")
        {
            _metaStarts?.TryAdd(element, position.Position - 1);
        }
    }

    /// <summary>The byte just past the '&gt;' that closes the tag starting at <paramref name="start"/>, in the encoding the page declares.</summary>
    private static int EndOfTag(string html, int start, string? charset)
    {
        var close = start >= 0 && start < html.Length ? html.IndexOf('>', start) : -1;
        return CharsetDecoder.ByteCount(html.AsSpan(0, close < 0 ? html.Length : close + 1), charset);
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
