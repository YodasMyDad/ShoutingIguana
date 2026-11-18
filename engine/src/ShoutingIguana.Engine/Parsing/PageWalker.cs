using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// One pass over a parsed document that collects every SEO fact and link. It tracks whether the walk is
/// in the head, which page region a link sits in, and which text counts as main content. Nav, footer and
/// head text are left out of the content, matching how duplicate content is usually judged.
/// </summary>
/// <param name="pageUri">The page's own URL, for telling links to the same page from links to the base URL.</param>
/// <param name="baseUri">What relative URLs resolve against: the first base href, or the page URL.</param>
internal sealed partial class PageWalker(Uri pageUri, Uri baseUri, string robotsToken, TextAccumulator text)
{
    private const int MAX_DEPTH = 512;
    private const int MAX_LABEL_CHARS = 200;
    private const int MAX_TITLE_CHARS = 1_000;
    private const int MAX_META_CHARS = 2_000;
    private const int MAX_HEADING_CHARS = 500;
    private const int MAX_LINKS = 10_000;
    private const int MAX_IMAGES = 2_000;
    private const int MAX_HEADINGS = 500;
    private const int MAX_META_TAGS = 200;
    private const int MAX_ICONS = 20;
    private const int MAX_ICON_ATTRIBUTE_CHARS = 100;
    internal const int MAX_IDS = 5_000;
    private const int MAX_JSON_LD_BLOCKS = 50;

    /// <summary>The forms a page's facts keep: enough for any page's search, sign-up and log-in.</summary>
    private const int MAX_FORMS = 20;
    private const int MAX_PAGINATION_LINKS = 20;
    private const int MAX_UNCRAWLABLE_EXAMPLES = 3;
    private const int MAX_UNCRAWLABLE_TEXT = 60;
    private const int MAX_HEAD_TAGS_IN_BODY = 20;
    private const int MAX_CHARSET_DECLARATIONS = 5;

    /// <summary>The most characters of an image's class a page's facts keep, for an image without a URL.</summary>
    private const int MAX_IMAGE_CLASS_CHARS = 100;

    /// <summary>Raw text read for a heading or link label, before whitespace is collapsed and the result capped.</summary>
    private const int MAX_RAW_LABEL_CHARS = 16 * MAX_HEADING_CHARS;
    /// <summary>Longer blocks are cut to this length, which leaves them unparseable; rules skip blocks this long.</summary>
    internal const int MAX_JSON_LD_CHARS = 200_000;
    private const string WHITESPACE = " \t\n\r\f";

    private static readonly HashSet<string> BLOCK_ELEMENTS = new(
    [
        "address", "article", "aside", "blockquote", "br", "dd", "div", "dl", "dt", "figcaption", "figure",
        "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p",
        "pre", "section", "table", "td", "th", "tr", "ul"
    ], StringComparer.Ordinal);

    private static readonly (string Token, LinkPosition Position)[] REGION_HINTS =
    [
        ("nav", LinkPosition.Navigation), ("navbar", LinkPosition.Navigation), ("navigation", LinkPosition.Navigation),
        ("menu", LinkPosition.Navigation), ("main-menu", LinkPosition.Navigation), ("main-nav", LinkPosition.Navigation),
        ("site-nav", LinkPosition.Navigation), ("breadcrumb", LinkPosition.Navigation), ("breadcrumbs", LinkPosition.Navigation),
        ("header", LinkPosition.Header), ("site-header", LinkPosition.Header), ("masthead", LinkPosition.Header),
        ("footer", LinkPosition.Footer), ("site-footer", LinkPosition.Footer),
        ("sidebar", LinkPosition.Sidebar)
    ];

    /// <summary>Attributes that make a <c>&lt;link rel="canonical"&gt;</c> an alternate's rather than the page's own, in the order a detail names them.</summary>
    private static readonly string[] CANONICAL_QUALIFIERS = ["hreflang", "lang", "media", "type"];

    /// <summary>Attributes lazy-loading scripts keep the real image URL in, in the order they're tried.</summary>
    private static readonly string[] LAZY_SOURCES = ["data-src", "data-lazy-src", "data-original", "data-lazy"];

    /// <summary>
    /// Attributes lazy-loading scripts keep an image's srcset in, which they copy to srcset as it scrolls into view: lazysizes'
    /// (Shopify's themes), and WordPress's lazy loaders'.
    /// </summary>
    private static readonly string[] LAZY_SRCSETS = ["data-srcset", "data-lazy-srcset"];

    private readonly string _pageUrl = UrlNormalizer.Normalize(pageUri);

    public List<string> Titles { get; } = [];
    public List<string> MetaDescriptions { get; } = [];
    public List<MetaTagFact> MetaTags { get; } = [];
    public List<string> RobotsMetaContents { get; } = [];
    public List<CanonicalFact> Canonicals { get; } = [];
    public List<HreflangFact> Hreflangs { get; } = [];
    public List<PaginationFact> Pagination { get; } = [];
    public List<HeadingFact> Headings { get; } = [];
    public List<ImageFact> Images { get; } = [];
    public List<string> JsonLd { get; } = [];
    public List<string> AnchorIds { get; } = [];
    public List<ExtractedLink> Links { get; } = [];
    public List<IconFact> Icons { get; } = [];
    public List<UncrawlableLinkFact> UncrawlableExamples { get; } = [];

    /// <summary>Head-only tags met in the body (<see cref="PageFacts.HeadTagsInBody"/>).</summary>
    public List<string> HeadTagsInBody { get; } = [];

    /// <summary>Every charset the page declares, in order (<see cref="PageFacts.CharsetDeclarations"/>).</summary>
    public List<string> CharsetDeclarations { get; } = [];

    /// <summary>Words in the body's text the content leaves out (navigation, header, footer, asides).</summary>
    public int TemplateWordCount { get; private set; }

    /// <summary>The page's forms, up to <see cref="MAX_FORMS"/>.</summary>
    public List<FormFact> Forms { get; } = [];

    /// <summary>How many <c>href</c> and <c>src</c> values start <c>//</c>, and the first.</summary>
    public int ProtocolRelativeCount { get; private set; }

    public string? FirstProtocolRelative { get; private set; }

    /// <summary>The first separate mobile version the page names (<see cref="PageFacts.MobileAlternate"/>).</summary>
    public string? MobileAlternate { get; private set; }

    /// <summary>Link-like elements with no address search engines follow, all of them; <see cref="UncrawlableExamples"/> holds the first few.</summary>
    public int UncrawlableCount { get; private set; }

    public MetaRefreshFact? MetaRefresh { get; private set; }
    public string? Language { get; private set; }
    public string? DeclaredCharset { get; private set; }

    /// <summary>The meta tag that declared <see cref="DeclaredCharset"/>, for the parser to say where it ends.</summary>
    public IElement? CharsetElement { get; private set; }

    public void Walk(IElement root) =>
        VisitElement(root, new WalkContext(LinkPosition.Content, InHead: false, ExcludeText: false, InArticle: false), 0);

    internal static string Clean(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(Math.Min(value.Length, maxChars));
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
            if (builder.Length >= maxChars)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private void VisitChildren(INode node, WalkContext context, int depth)
    {
        if (depth > MAX_DEPTH)
        {
            return;
        }

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is IElement element)
            {
                VisitElement(element, context, depth + 1);
            }
            else if (child is IText textNode && !context.ExcludeText)
            {
                text.Append(textNode.Data);
            }
            else if (child is IText templateNode && !context.InHead)
            {
                // Text the content leaves out (menus, header, footer): words of the page all the same.
                TemplateWordCount += TextMeasures.Words(templateNode.Data);
            }
        }
    }

    private void VisitElement(IElement element, WalkContext context, int depth)
    {
        // SVG and MathML carry no signals we use, and their <title>/<a> elements are not page titles or links, but
        // their ids are places a #fragment can point to.
        if (element.NamespaceUri != NamespaceNames.HtmlUri)
        {
            AddForeignIds(element, depth);
            return;
        }

        // Before any early return: #photo can point at an <img id="photo"> or #map at an <iframe id="map">.
        if (element.Id is { Length: > 0 } id)
        {
            AddAnchorId(id);
        }

        var name = element.LocalName;

        // An href or src written without its scheme ("//cdn.example.com/app.js") takes the page's.
        foreach (var attribute in (string[])["href", "src"])
        {
            if (element.GetAttribute(attribute)?.TrimStart() is { } value && value.StartsWith("//", StringComparison.Ordinal) && value.Length > 2)
            {
                ProtocolRelativeCount++;
                FirstProtocolRelative ??= value.Length > MAX_LABEL_CHARS ? value[..MAX_LABEL_CHARS] : value;
            }
        }

        if (name == "form" && Forms.Count < MAX_FORMS)
        {
            var action = element.GetAttribute("action");
            var target = string.IsNullOrWhiteSpace(action) ? baseUri : UrlNormalizer.TryResolve(baseUri, action, out var resolved) ? resolved : null;
            Forms.Add(new FormFact(target is null ? null : UrlNormalizer.Normalize(target), NullIfBlank(element.GetAttribute("method"))?.Trim().ToLowerInvariant() ?? "get",
                element.QuerySelector("input[type=password i]") is not null));
        }

        // Only <a>, <area>, <link> and <base> take an href: on a <span> or <div>, one that leads somewhere is a link search
        // engines can't follow (a widget's href="javascript:void(0)" or "#reviews" leads nowhere they could go).
        if (name is not ("a" or "area" or "link" or "base") && LeadsSomewhere(element.GetAttribute("href")))
        {
            AddUncrawlable(element, $"<{name} href>");
        }

        var childContext = context;
        switch (name)
        {
            case "head":
                childContext = context with { InHead = true, Position = LinkPosition.Head, ExcludeText = true };
                break;
            case "body":
                childContext = context with { InHead = false, Position = LinkPosition.Content, ExcludeText = false };
                break;
            case "html":
                Language ??= NullIfBlank(element.GetAttribute("lang"))?.Trim();
                break;
            case "title":
                Titles.Add(Clean(element.TextContent, MAX_TITLE_CHARS));
                InBody(context, "title");
                return;
            case "meta":
                AddMeta(element, context);
                return;
            case "link":
                AddLinkElement(element, context);
                return;
            case "script":
                AddScript(element, context);
                return;
            case "style" or "template" or "base":
                return;
            case "noscript":
                childContext = context with { ExcludeText = true };
                break;
            case "img":
                AddImage(element, context);
                return;
            case "iframe":
                AddLink(element.GetAttribute("src"), LinkKind.Iframe, LinkFlags.None, context.Position, null);
                return;
            case "video" or "audio" or "source" or "track":
                AddLink(element.GetAttribute("src"), LinkKind.Media, LinkFlags.None, context.Position, null);
                break;
            case "a" or "area":
                AddAnchor(element, context);
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (Headings.Count < MAX_HEADINGS)
                {
                    var (headingText, fromImage) = HeadingText(element);
                    Headings.Add(new HeadingFact(name[1] - '0', headingText) { FromImageAlt = fromImage });
                }

                break;
            case "nav":
                childContext = context with { Position = LinkPosition.Navigation, ExcludeText = true };
                break;
            case "footer" when !context.InArticle:
                childContext = context with { Position = LinkPosition.Footer, ExcludeText = true };
                break;
            case "header" when !context.InArticle:
                childContext = context with { Position = LinkPosition.Header };
                break;
            case "aside":
                childContext = context with { Position = LinkPosition.Sidebar };
                break;
            case "main":
                childContext = context with { Position = LinkPosition.Content, InArticle = true };
                break;
            case "article":
                childContext = context with { InArticle = true };
                break;
            default:
                if (context.Position == LinkPosition.Content)
                {
                    childContext = ApplyRegionHints(element, context);
                }

                break;
        }

        var isBlock = BLOCK_ELEMENTS.Contains(name);
        if (isBlock)
        {
            text.AppendSeparator();
        }

        VisitChildren(element, childContext, depth);

        if (isBlock)
        {
            text.AppendSeparator();
        }
    }

    private void AddForeignIds(IElement element, int depth)
    {
        if (element.Id is { Length: > 0 } id)
        {
            AddAnchorId(id);
        }

        for (var child = element.FirstElementChild; child is not null && depth < MAX_DEPTH; child = child.NextElementSibling)
        {
            AddForeignIds(child, depth + 1);
        }
    }

    /// <summary>A heading's text, or for a logo heading (an image and no text) the image's alt, as screen readers announce it.</summary>
    private static (string Text, bool FromImageAlt) HeadingText(IElement heading)
    {
        var text = ReadableText(heading, MAX_HEADING_CHARS);
        return text.Length == 0 && heading.QuerySelector("img[alt]")?.GetAttribute("alt") is { } alt
            ? (Clean(alt, MAX_HEADING_CHARS), true)
            : (text, false);
    }

    /// <summary>
    /// An element's text as it reads on screen: a &lt;br&gt; or block element between words separates them, as in
    /// the main-content text, instead of gluing "Welcome to&lt;br&gt;Our Store" into "Welcome toOur Store".
    /// </summary>
    private static string ReadableText(IElement element, int maxChars)
    {
        var builder = new System.Text.StringBuilder();
        AppendReadableText(element, builder, 0);
        return Clean(builder.ToString(), maxChars);
    }

    private static void AppendReadableText(INode node, System.Text.StringBuilder builder, int depth)
    {
        for (var child = node.FirstChild; child is not null && builder.Length < MAX_RAW_LABEL_CHARS; child = child.NextSibling)
        {
            if (child is IText textNode)
            {
                builder.Append(textNode.Data);
            }
            else if (child is IElement element && element.LocalName is not ("script" or "style" or "template") && depth < MAX_DEPTH)
            {
                var isBlock = element.NamespaceUri == NamespaceNames.HtmlUri && BLOCK_ELEMENTS.Contains(element.LocalName);
                if (isBlock)
                {
                    builder.Append(' ');
                }

                AppendReadableText(element, builder, depth + 1);
                if (isBlock)
                {
                    builder.Append(' ');
                }
            }
        }
    }

    private static WalkContext ApplyRegionHints(IElement element, WalkContext context)
    {
        var region = element.GetAttribute("role") switch
        {
            "navigation" => LinkPosition.Navigation,
            "banner" => LinkPosition.Header,
            "contentinfo" => LinkPosition.Footer,
            "complementary" => LinkPosition.Sidebar,
            _ => RegionFromTokens(element.Id) ?? RegionFromTokens(element.ClassName) ?? LinkPosition.Content
        };

        if (region == LinkPosition.Content || (context.InArticle && region is LinkPosition.Header or LinkPosition.Footer))
        {
            return context;
        }

        return context with
        {
            Position = region,
            ExcludeText = context.ExcludeText || region is LinkPosition.Navigation or LinkPosition.Footer
        };
    }

    private static LinkPosition? RegionFromTokens(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var span = value.AsSpan();
        foreach (var range in span.SplitAny(WHITESPACE))
        {
            var token = span[range];
            foreach (var (hint, position) in REGION_HINTS)
            {
                if (token.Equals(hint, StringComparison.OrdinalIgnoreCase))
                {
                    return position;
                }
            }
        }

        return null;
    }

    /// <summary>The first declaration counts, as in browsers; every one is kept, for a page that declares two.</summary>
    private void DeclareCharset(string charset, IElement element)
    {
        if (DeclaredCharset is null)
        {
            DeclaredCharset = charset;
            CharsetElement = element;
        }

        if (CharsetDeclarations.Count < MAX_CHARSET_DECLARATIONS)
        {
            CharsetDeclarations.Add(charset);
        }
    }

    /// <summary>Notes a head-only tag the parser put in the body, which search engines may ignore there.</summary>
    private void InBody(WalkContext context, string tag)
    {
        if (!context.InHead && HeadTagsInBody.Count < MAX_HEAD_TAGS_IN_BODY)
        {
            HeadTagsInBody.Add(tag);
        }
    }

    private void AddMeta(IElement element, WalkContext context)
    {
        var content = element.GetAttribute("content");
        if (NullIfBlank(element.GetAttribute("charset")) is { } charset)
        {
            DeclareCharset(charset.Trim(), element);
        }

        var httpEquiv = element.GetAttribute("http-equiv")?.Trim().ToLowerInvariant();
        if (httpEquiv is "refresh" or "content-type")
        {
            InBody(context, $"meta {httpEquiv}");
        }

        if (httpEquiv == "refresh" && content is not null)
        {
            if (MetaRefresh is null)
            {
                MetaRefresh = ParseRefresh(content);
                AddLink(MetaRefresh.Url, LinkKind.MetaRefresh, LinkFlags.None, LinkPosition.Head, null);
            }

            return;
        }

        if (httpEquiv == "content-type" && content is not null)
        {
            var charsetAt = content.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (charsetAt >= 0)
            {
                DeclareCharset(content[(charsetAt + 8)..].Trim().Trim('"', '\''), element);
            }

            return;
        }

        // One tag can serve both: <meta name="description" property="og:description" content="...">.
        var name = NullIfBlank(element.GetAttribute("name"))?.Trim().ToLowerInvariant();
        var property = NullIfBlank(element.GetAttribute("property"))?.Trim().ToLowerInvariant();
        if ((name is null && property is null) || content is null)
        {
            return;
        }

        if (name is "description" or "robots" or "googlebot" or "viewport" || (name is not null && RobotsMetaParser.AppliesToMetaName(name, robotsToken)))
        {
            InBody(context, $"meta {name}");
        }
        else if (new[] { property, name }.FirstOrDefault(tag => tag is not null && (tag.StartsWith("og:", StringComparison.Ordinal) ||
                                                                                   tag.StartsWith("twitter:", StringComparison.Ordinal))) is { } social)
        {
            InBody(context, social);
        }

        var cleaned = Clean(content, MAX_META_CHARS);
        AddMetaTag(name, cleaned);
        if (property != name)
        {
            AddMetaTag(property, cleaned, isProperty: true);
        }

        // A share image given as a full URL is fetched as a file, so its checks can judge it; one given in part is
        // social-image-relative's, and isn't guessed at.
        if (new[] { name, property }.FirstOrDefault(tag => tag is "og:image" or "og:image:url" or "og:image:secure_url" or "twitter:image") is { } imageTag &&
            Uri.TryCreate(cleaned.Trim(), UriKind.Absolute, out var image) && image.Scheme is "http" or "https" &&
            !cleaned.TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            AddLink(cleaned.Trim(), LinkKind.SocialImage, LinkFlags.None, LinkPosition.Head, imageTag);
        }

        // Search engines read the description and robots directives from name; property is RDFa, as Open Graph uses.
        if (name == "description")
        {
            MetaDescriptions.Add(cleaned);
        }
        else if (name is not null && RobotsMetaParser.AppliesToMetaName(name, robotsToken))
        {
            RobotsMetaContents.Add(content);
        }
    }

    private void AddMetaTag(string? name, string content, bool isProperty = false)
    {
        if (name is not null && MetaTags.Count < MAX_META_TAGS)
        {
            MetaTags.Add(new MetaTagFact(name, content) { IsProperty = isProperty });
        }
    }

    private void AddLinkElement(IElement element, WalkContext context)
    {
        var rel = element.GetAttribute("rel");
        var href = element.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(rel) || href is null)
        {
            return;
        }

        var flags = context.InHead ? LinkFlags.None : LinkFlags.OutsideHead;
        var position = context.InHead ? LinkPosition.Head : context.Position;
        var tokens = rel.AsSpan();

        foreach (var range in tokens.SplitAny(WHITESPACE))
        {
            var token = tokens[range];
            if (token.Equals("canonical", StringComparison.OrdinalIgnoreCase))
            {
                Canonicals.Add(new CanonicalFact(href, NormalizeOrNull(href), context.InHead, FromHttpHeader: false) { Attributes = CanonicalQualifiers(element) });
                AddLink(href, LinkKind.Canonical, flags, position, null);
            }
            else if (token.Equals("alternate", StringComparison.OrdinalIgnoreCase) &&
                     NullIfBlank(element.GetAttribute("hreflang")) is { } language)
            {
                var code = language.Trim();
                Hreflangs.Add(new HreflangFact(code, href, NormalizeOrNull(href), context.InHead, FromHttpHeader: false));
                AddLink(href, LinkKind.Hreflang, flags, position, code);
            }
            else if (token.Equals("alternate", StringComparison.OrdinalIgnoreCase))
            {
                // A media query names a separate version for some screens: a mobile site's.
                if (element.HasAttribute("media"))
                {
                    MobileAlternate ??= NormalizeOrNull(href);
                }

                InBody(context, "link alternate");
            }
            else if (token.Equals("next", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("prev", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("previous", StringComparison.OrdinalIgnoreCase))
            {
                if (Pagination.Count < MAX_PAGINATION_LINKS)
                {
                    Pagination.Add(new PaginationFact(token.Equals("next", StringComparison.OrdinalIgnoreCase), NormalizeOrNull(href)));
                }

                AddLink(href, LinkKind.Pagination, flags, position, null);
            }
            else if (token.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                AddLink(href, LinkKind.Stylesheet, LinkFlags.None, position, null);
            }
            else if (token.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase))
            {
                AddLink(href, LinkKind.Icon, LinkFlags.None, position, null);

                // The site's own icon, as a browser tab shows it, is a rel="icon": a touch icon is the home screen's.
                if (token.Equals("icon", StringComparison.OrdinalIgnoreCase) && Icons.Count < MAX_ICONS && UrlNormalizer.TryResolve(baseUri, href, out var icon))
                {
                    Icons.Add(new IconFact(icon.AbsoluteUri, IconAttribute(element, "sizes"), IconAttribute(element, "type")));
                }
            }
            else if (token.Equals("amphtml", StringComparison.OrdinalIgnoreCase))
            {
                AddLink(href, LinkKind.Hyperlink, LinkFlags.None, position, null);
                InBody(context, "link amphtml");
            }
        }
    }

    /// <summary>The canonical's attributes that qualify it (<see cref="CANONICAL_QUALIFIERS"/>), comma-separated, or null with none.</summary>
    private static string? CanonicalQualifiers(IElement element)
    {
        var present = CANONICAL_QUALIFIERS.Where(element.HasAttribute).ToList();
        return present.Count == 0 ? null : string.Join(", ", present);
    }

    /// <summary>What an icon's link says of it, trimmed and cut short: a size or a media type is a few characters.</summary>
    private static string? IconAttribute(IElement element, string name) =>
        NullIfBlank(element.GetAttribute(name)) is { } value ? NullIfBlank(Clean(value, MAX_ICON_ATTRIBUTE_CHARS)) : null;

    private void AddScript(IElement element, WalkContext context)
    {
        var type = element.GetAttribute("type")?.Trim();
        if (string.Equals(type, "application/ld+json", StringComparison.OrdinalIgnoreCase))
        {
            if (JsonLd.Count < MAX_JSON_LD_BLOCKS)
            {
                var json = element.TextContent.Trim();
                JsonLd.Add(json.Length > MAX_JSON_LD_CHARS ? json[..MAX_JSON_LD_CHARS] : json);
            }

            return;
        }

        AddLink(element.GetAttribute("src"), LinkKind.Script, LinkFlags.None, context.InHead ? LinkPosition.Head : context.Position, null);
    }

    private void AddAnchor(IElement element, WalkContext context)
    {
        if (NullIfBlank(element.GetAttribute("name")) is { } anchorName)
        {
            AddAnchorId(anchorName);
        }

        var href = element.GetAttribute("href");
        if (element.LocalName == "a" && UncrawlableBy(element, href) is { } why)
        {
            AddUncrawlable(element, why);
        }

        if (href is null)
        {
            return;
        }

        var flags = RelFlags(element.GetAttribute("rel"));

        // An image map's <area> has no content; its alt text is its label.
        var label = element.LocalName == "area" ? Clean(element.GetAttribute("alt"), MAX_LABEL_CHARS) : ReadableText(element, MAX_LABEL_CHARS);
        if (label.Length == 0)
        {
            if (FindFirstImage(element, 0) is not null)
            {
                flags |= LinkFlags.ImageLink;
            }

            label = NameWithoutText(element);
        }

        if (!UrlNormalizer.TryResolve(baseUri, href, out var target))
        {
            return;
        }

        // Same page by where it leads, not how it's written: under a <base> on another URL, #reviews is a link to the
        // base URL (HTML spec), while href="page#reviews" on /dir/page stays on the page.
        if (UrlNormalizer.Normalize(target) == _pageUrl)
        {
            flags |= LinkFlags.SamePage;
        }

        AddLink(target, LinkKind.Hyperlink, flags, context.Position, label.Length == 0 ? null : label);
    }

    /// <summary>
    /// What keeps search engines from following an <c>&lt;a&gt;</c> that leads somewhere, as Google's "Make your links
    /// crawlable" lists them: a <c>javascript:</c> href whose script navigates; no href, an empty one or a bare <c>#</c>, and
    /// an onclick that navigates; no href and a routerLink. Null for one it can follow, for a named anchor, and for script
    /// that goes nowhere (a menu toggle, <c>javascript:void(0)</c>): there's no page behind it to miss. An empty href
    /// alone is a link to the page itself.
    /// </summary>
    private static string? UncrawlableBy(IElement anchor, string? href)
    {
        var written = href?.Trim();
        var onclickNavigates = Navigates(anchor.GetAttribute("onclick"));
        return written switch
        {
            null or "" or "#" when onclickNavigates => "onclick",
            null when anchor.HasAttribute("routerlink") => "routerLink",
            null => null,
            _ when written.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) && Navigates(written["javascript:".Length..]) => "javascript:",
            _ => null
        };
    }

    /// <summary>
    /// An href on an element that can't take one (a <c>&lt;span&gt;</c>, a <c>&lt;div&gt;</c>) leads somewhere: a URL, or
    /// script that navigates. Not an empty one, a fragment, or script that goes nowhere.
    /// </summary>
    private static bool LeadsSomewhere(string? href) => href?.Trim() is { Length: > 0 } written && written[0] != '#' &&
        (!written.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || Navigates(written["javascript:".Length..]));

    /// <summary>
    /// Script that takes the visitor to another page, as far as its text shows: it sets the location or an href, opens a
    /// window, calls something named navigate or goto, or quotes a path or URL. A toggle, <c>void(0)</c> or <c>next()</c> doesn't.
    /// </summary>
    private static bool Navigates(string? script) => script is not null && NavigatingScript().IsMatch(script);

    [GeneratedRegex("""\blocation\b|\.href\b|\bwindow\.open\b|\bnavigate\w*|\bgo_?to\b|['"](?:/|https?://|\.\.?/)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NavigatingScript();

    private void AddUncrawlable(IElement element, string why)
    {
        UncrawlableCount++;
        if (UncrawlableExamples.Count < MAX_UNCRAWLABLE_EXAMPLES)
        {
            var text = ReadableText(element, MAX_UNCRAWLABLE_TEXT);
            UncrawlableExamples.Add(new UncrawlableLinkFact(text.Length > 0 ? text : NameWithoutText(element), why));
        }
    }

    private void AddImage(IElement element, WalkContext context)
    {
        // Lazy loaders keep a placeholder (or nothing) in src and the real image elsewhere; srcset alone is valid too.
        var source = ImageSource(element.GetAttribute("src"));
        foreach (var attribute in LAZY_SOURCES)
        {
            source ??= ImageSource(element.GetAttribute(attribute));
        }

        source ??= FirstCandidate(element.GetAttribute("srcset"));
        foreach (var attribute in LAZY_SRCSETS)
        {
            source ??= FirstCandidate(element.GetAttribute(attribute));
        }

        // The URL is resolved past the link cap too, so alt text and dimensions are still checked.
        var alt = element.GetAttribute("alt");
        string? url = null;
        if (source is not null && UrlNormalizer.TryResolve(baseUri, source, out var target))
        {
            url = UrlNormalizer.Normalize(target);
            if (Links.Count < MAX_LINKS)
            {
                Links.Add(new ExtractedLink(target, LinkKind.Image, LinkFlags.None, context.Position, NullIfBlank(Clean(alt, MAX_LABEL_CHARS))));
            }
        }

        if (Images.Count < MAX_IMAGES)
        {
            var data = url is null ? DataUriBytes(element.GetAttribute("src")) : null;
            Images.Add(new ImageFact(
                url,
                alt is null ? null : Clean(alt, MAX_LABEL_CHARS),
                alt is not null,
                NullIfBlank(element.GetAttribute("width")),
                NullIfBlank(element.GetAttribute("height")),
                HasSrcset(element),
                NullIfBlank(element.GetAttribute("loading")))
            {
                IsDataUri = data is not null,
                DataUriBytes = data,
                InPicture = element.ParentElement is { LocalName: "picture" } picture &&
                            picture.Children.Any(child => child.LocalName == "source" && SourceSrcset(child) is not null),

                // What tells apart an image with no file to name: only those keep it, so a page of photos stores no more.
                Class = url is null ? NullIfBlank(Clean(element.GetAttribute("class"), MAX_IMAGE_CLASS_CHARS)) : null,
                Position = url is null ? context.Position : null
            });
        }
    }

    /// <summary>The image has a srcset, or one a lazy-loading script will copy there (<see cref="LAZY_SRCSETS"/>): an empty one too, which a script may fill.</summary>
    private static bool HasSrcset(IElement image) => image.HasAttribute("srcset") || LAZY_SRCSETS.Any(image.HasAttribute);

    /// <summary>A picture's &lt;source&gt;'s srcset, or the lazy-loaded one, when it names a file; null otherwise.</summary>
    private static string? SourceSrcset(IElement source) =>
        NullIfBlank(source.GetAttribute("srcset")) ?? LAZY_SRCSETS.Select(source.GetAttribute).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private void AddLink(string? href, LinkKind kind, LinkFlags flags, LinkPosition position, string? label)
    {
        if (href is not null && UrlNormalizer.TryResolve(baseUri, href, out var target))
        {
            AddLink(target, kind, flags, position, label);
        }
    }

    private void AddLink(Uri target, LinkKind kind, LinkFlags flags, LinkPosition position, string? label)
    {
        if (Links.Count < MAX_LINKS)
        {
            Links.Add(new ExtractedLink(target, kind, flags, position, label));
        }
    }

    /// <summary>An image URL as written, or null for none and for an inline data: placeholder.</summary>
    /// <summary>
    /// A <c>data:</c> src's size once decoded: its payload after the comma, three bytes for every four characters of
    /// base64 (less its padding), one for each character otherwise. Null for a src that isn't a data: URI.
    /// </summary>
    private static long? DataUriBytes(string? source)
    {
        var value = source?.Trim();
        if (value is null || !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return 0;
        }

        var payload = value.AsSpan(comma + 1).Trim();
        var isBase64 = value.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        return isBase64 ? Math.Max(0, payload.Length * 3L / 4 - payload.Length + payload.TrimEnd('=').Length) : payload.Length;
    }

    private static string? ImageSource(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? null : value;

    /// <summary>
    /// The URL of the first srcset candidate: the first run of non-space characters, less trailing commas, as the HTML
    /// spec parses it (a URL may contain commas, as image CDNs' w_100,h_100 do).
    /// </summary>
    private static string? FirstCandidate(string? srcset)
    {
        var rest = srcset.AsSpan().TrimStart(" \t\n\r\f,");
        var end = rest.IndexOfAny(WHITESPACE);
        return ImageSource((end < 0 ? rest : rest[..end]).TrimEnd(',').ToString());
    }

    private void AddAnchorId(string id)
    {
        if (AnchorIds.Count < MAX_IDS)
        {
            AnchorIds.Add(id);
        }
    }

    private string? NormalizeOrNull(string href) =>
        UrlNormalizer.TryResolve(baseUri, href, out var target) ? UrlNormalizer.Normalize(target) : null;

    /// <summary>A refresh's delay and URL, as a meta tag's content or a <c>Refresh</c> header writes them: <c>5; url=/next</c>.</summary>
    internal static MetaRefreshFact ParseRefresh(string content)
    {
        var separator = content.IndexOfAny([';', ',']);
        var delayText = (separator < 0 ? content : content[..separator]).Trim();
        int? delay = int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : null;
        if (separator < 0)
        {
            return new MetaRefreshFact(delay, null);
        }

        var target = content[(separator + 1)..].Trim();
        if (target.StartsWith("url", StringComparison.OrdinalIgnoreCase))
        {
            var equals = target.IndexOf('=');
            if (equals >= 0)
            {
                target = target[(equals + 1)..];
            }
        }

        target = target.Trim().Trim('"', '\'').Trim();
        return new MetaRefreshFact(delay, target.Length == 0 ? null : target);
    }

    private static LinkFlags RelFlags(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel))
        {
            return LinkFlags.None;
        }

        var flags = LinkFlags.None;
        var tokens = rel.AsSpan();
        foreach (var range in tokens.SplitAny(WHITESPACE))
        {
            var token = tokens[range];
            if (token.Equals("nofollow", StringComparison.OrdinalIgnoreCase))
            {
                flags |= LinkFlags.NoFollow;
            }
            else if (token.Equals("sponsored", StringComparison.OrdinalIgnoreCase))
            {
                flags |= LinkFlags.Sponsored;
            }
            else if (token.Equals("ugc", StringComparison.OrdinalIgnoreCase))
            {
                flags |= LinkFlags.Ugc;
            }
        }

        return flags;
    }

    /// <summary>
    /// What a screen reader announces for a link without text, and what search engines take as its anchor text: the alt
    /// text of an image in it (the first that has some) or a labelled child such as an icon's SVG, else the link's own
    /// aria-labelledby, aria-label or title.
    /// </summary>
    private static string NameWithoutText(IElement link)
    {
        var name = link.QuerySelectorAll("img[alt], [aria-label]")
                       .Select(child => child.LocalName == "img" ? child.GetAttribute("alt") : child.GetAttribute("aria-label"))
                       .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                   ?? LabelledBy(link)
                   ?? NullIfBlank(link.GetAttribute("aria-label"))
                   ?? link.GetAttribute("title");
        return Clean(name, MAX_LABEL_CHARS);
    }

    /// <summary>The text of the elements aria-labelledby names, joined in order as screen readers read them.</summary>
    private static string? LabelledBy(IElement element) =>
        element.GetAttribute("aria-labelledby") is { } ids && element.Owner is { } document
            ? NullIfBlank(string.Join(' ', ids.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(document.GetElementById).OfType<IElement>().Select(label => ReadableText(label, MAX_LABEL_CHARS))))
            : null;

    private static IElement? FindFirstImage(INode node, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is not IElement element)
            {
                continue;
            }

            if (element.LocalName == "img")
            {
                return element;
            }

            if (FindFirstImage(element, depth + 1) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
