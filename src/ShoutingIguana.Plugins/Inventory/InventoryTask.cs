using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Inventory;

/// <summary>
/// Inventory task - tracks basic URL information (already handled by crawl engine).
/// This plugin mainly provides error findings.
/// </summary>
public class InventoryTask : UrlTaskBase
{
    public override string Key => "Inventory";
    public override string DisplayName => "URL Inventory";
    public override int Priority => 10; // Run first

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only report errors - success is already tracked in Url table
        // Don't create findings for successful crawls to avoid massive database bloat
        if (ctx.Metadata.StatusCode >= 400)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                $"HTTP_{ctx.Metadata.StatusCode}",
                $"HTTP error {ctx.Metadata.StatusCode} for {ctx.Url}",
                new
                {
                    status = ctx.Metadata.StatusCode,
                    depth = ctx.Metadata.Depth
                });
        }
        
        await Task.CompletedTask;
    }
}

