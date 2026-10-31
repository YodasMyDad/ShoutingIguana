using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Turns resolved link targets into writer-ready links: normalised URL, hash key and scope. This runs on
/// the workers in parallel, so the single writer only does dictionary lookups.
/// </summary>
internal sealed class OutLinkFactory(CrawlScope scope)
{
    private const int MAX_TEXT_CHARS = 200;

    public OutLink Create(Uri target, LinkKind kind, LinkFlags flags, LinkPosition position, string? text)
    {
        var url = UrlNormalizer.Normalize(target);
        var isInternal = scope.IsInternal(target);
        var isCrawlable = isInternal && kind.IsPage() && scope.IsInStartFolder(target);
        var label = text is { Length: > MAX_TEXT_CHARS } ? text[..MAX_TEXT_CHARS] : text;
        return new OutLink(url, UrlKey.Compute(url), isInternal, isCrawlable, kind, flags, position, label, UrlNormalizer.GetFragment(target));
    }

    public OutLink Create(ExtractedLink link) => Create(link.Target, link.Kind, link.Flags, link.Position, link.Text);
}
