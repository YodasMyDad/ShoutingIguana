using System.Text;
using HtmlAgilityPack;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.Inventory;

/// <summary>
/// Emits one row per crawled internal URL describing its on-page metadata
/// (title, description, H1), server-side signals (canonical, robots, language,
/// content length), and a computed <see cref="Indexable"/> verdict.
/// </summary>
public class InventoryTask : UrlTaskBase
{
    public override string Key => "Inventory";
    public override string DisplayName => "Inventory";
    public override string Description => "Records on-page and server-side metadata for each crawled URL.";
    public override int Priority => 10; // Run first so later tasks can't muddy the inventory.

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        var status = ctx.Metadata.StatusCode;

        // Inventory only tracks responses we actually retrieved. 4xx/5xx are already
        // reported by BrokenLinks, and inventorying them adds noise with no signal.
        if (status < 200 || status >= 400)
            return;

        // External URLs are crawled for status-code checks only; their HTML isn't ours
        // to inventory. Stay consistent with the rest of the plugin suite.
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
            return;

        var isHtml = ctx.Metadata.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

        var title = "";
        var description = "";
        var h1 = "";
        long? htmlByteCount = null;

        if (isHtml && !string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
            description = doc.DocumentNode
                .SelectSingleNode("//meta[translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='description']")
                ?.GetAttributeValue("content", "")?.Trim() ?? "";
            h1 = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "";

            htmlByteCount = Encoding.UTF8.GetByteCount(ctx.RenderedHtml);
        }

        var canonical = !string.IsNullOrEmpty(ctx.Metadata.CanonicalHtml)
            ? ctx.Metadata.CanonicalHtml
            : ctx.Metadata.CanonicalHttp ?? "";

        var contentLength = ctx.Metadata.ContentLength ?? htmlByteCount;

        var row = ReportRow.Create()
            .Set("URL", ctx.Url.ToString())
            .Set("Title", title)
            .Set("Description", description)
            .Set("H1", h1)
            .Set("Canonical", canonical)
            .Set("Noindex", ctx.Metadata.RobotsNoindex == true ? "Yes" : "No")
            .Set("Nofollow", ctx.Metadata.RobotsNofollow == true ? "Yes" : "No")
            .Set("ContentType", ctx.Metadata.ContentType ?? "")
            .Set("Status", status)
            .Set("ContentLength", contentLength ?? 0)
            .Set("Depth", ctx.Metadata.Depth)
            .Set("Language", ctx.Metadata.HtmlLang ?? "")
            .Set("CrawledUtc", ctx.Metadata.CrawledUtc)
            .Set("Indexable", IsIndexable(ctx, isHtml) ? "Yes" : "No");

        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, ct);
    }

    private static bool IsIndexable(UrlContext ctx, bool isHtml)
    {
        if (!isHtml) return false;

        var status = ctx.Metadata.StatusCode;
        if (status < 200 || status >= 300) return false;

        // RobotsNoindex already rolls up <meta name="robots"> and X-Robots-Tag,
        // taking the most restrictive value when they conflict.
        if (ctx.Metadata.RobotsNoindex == true) return false;

        return true;
    }
}
