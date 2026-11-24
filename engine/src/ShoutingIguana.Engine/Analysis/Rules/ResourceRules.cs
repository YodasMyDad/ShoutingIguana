using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The files pages load, judged by how they're used: a script, stylesheet or image served as another type, and a script
/// or stylesheet past the 2 MB Googlebot reads of a file. That limit is of the file once decompressed, so a file is
/// judged by that size where the crawl counted it, and by the bytes sent in a crawl from before it did. A URL is
/// reported once, however many pages use it; only the URLs at fault are kept.
/// </summary>
/// <param name="readContentType">The Content-Type a stored URL answered with, as the crawl recorded it, or null.</param>
internal sealed class ResourceRules(UrlTable urls, Func<int, string?> readContentType) : ISiteRule
{
    /// <summary>How much of each file Googlebot reads for Google Search, as of the page itself (Search Central, the Googlebot doc).</summary>
    private const long GOOGLEBOT_FILE_BYTES = PerformanceRules.GOOGLEBOT_HTML_BYTES;

    // The ways each mismatched URL is used that its type doesn't fit, and the scripts and stylesheets past the limit.
    private readonly Dictionary<int, SortedSet<LinkKind>> _mismatched = [];
    private readonly SortedSet<int> _pastLimit = [];

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.CONTENT_TYPE_MISMATCH, IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT];

    public void Observe(AnalyzedPage page)
    {
        foreach (var link in page.Links)
        {
            // A stray tag naming the page itself (a stylesheet link whose href is "#") is no file sent with the wrong type.
            if (link.Kind is not (LinkKind.Script or LinkKind.Stylesheet or LinkKind.Image) || link.ToId == page.UrlId ||
                urls[link.ToId] is not { Fetched: true, Outcome: FetchOutcome.Success } file)
            {
                continue;
            }

            if (!Fits(link.Kind, file.ContentClass))
            {
                if (!_mismatched.TryGetValue(link.ToId, out var uses))
                {
                    _mismatched[link.ToId] = uses = [];
                }

                uses.Add(link.Kind);
            }

            if (link.Kind is LinkKind.Script or LinkKind.Stylesheet && (file.BodyBytes ?? file.SizeBytes) > GOOGLEBOT_FILE_BYTES)
            {
                _pastLimit.Add(link.ToId);
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var (id, uses) in _mismatched.OrderBy(entry => entry.Key))
        {
            var type = readContentType(id);
            var sent = urls[id].ContentClass switch
            {
                ContentClass.Missing => "sent with no Content-Type",
                _ => $"sent as {RuleText.Cut(type ?? "?", 60)}"
            };
            yield return new IssueHit(IssueCatalog.CONTENT_TYPE_MISMATCH, id, $"Used as {string.Join(" and ", uses.OrderBy(UseOrder).Select(Use))}, {sent}");
        }

        foreach (var id in _pastLimit)
        {
            yield return new IssueHit(IssueCatalog.RESOURCE_PAST_CRAWL_LIMIT, id,
                $"{Measured(urls[id])}, past the {RuleText.LimitSize(GOOGLEBOT_FILE_BYTES)} Google reads");
        }
    }

    /// <summary>
    /// The size a file is judged by. One sent compressed says so, with the bytes sent beside it, because the Size the
    /// URL is listed with is the bytes sent: "2.3 MB once decompressed (586 KB as sent)".
    /// </summary>
    private static string Measured(UrlStatus file)
    {
        if (file.BodyBytes is not { } decompressed)
        {
            return RuleText.Size(file.SizeBytes!.Value);
        }

        var size = PerformanceRules.Measured(decompressed, GOOGLEBOT_FILE_BYTES);
        return file.SizeBytes switch
        {
            null => $"{size} once decompressed",
            { } sent when sent != decompressed => $"{size} once decompressed ({RuleText.Size(sent)} as sent)",
            _ => size
        };
    }

    /// <summary>The type a file is sent as suits how the page uses it: a script as JavaScript, a stylesheet as CSS, an image as an image.</summary>
    private static bool Fits(LinkKind use, ContentClass type) => use switch
    {
        LinkKind.Script => type == ContentClass.Script,
        LinkKind.Stylesheet => type == ContentClass.Stylesheet,
        _ => type == ContentClass.Image
    };

    /// <summary>Scripts first, then stylesheets, then images, as a detail names them.</summary>
    private static int UseOrder(LinkKind use) => use switch
    {
        LinkKind.Script => 0,
        LinkKind.Stylesheet => 1,
        _ => 2
    };

    private static string Use(LinkKind use) => use switch
    {
        LinkKind.Script => "a script",
        LinkKind.Stylesheet => "a stylesheet",
        _ => "an image"
    };
}
