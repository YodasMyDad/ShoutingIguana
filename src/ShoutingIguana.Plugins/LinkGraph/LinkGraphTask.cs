using System;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.LinkGraph;

/// <summary>
/// Generates findings for each internal link to visualize the link graph.
/// Each link becomes an Info-level finding showing FROM URL -> TO URL with anchor text.
/// </summary>
public class LinkGraphTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;

    public override string Key => "LinkGraph";
    public override string DisplayName => "Link Graph";
    public override string Description => "Internal linking structure visualization";
    public override int Priority => 1000; // Run after other analysis tasks

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        // This task runs once per URL crawled and generates findings
        // for all outgoing links FROM this URL
        
        var fromUrlId = ctx.Metadata.UrlId;
        var fromUrlAddress = ctx.Url.ToString();
        
        // Get outgoing links from this URL using the efficient SDK API
        var outgoingLinks = await _repositoryAccessor.GetLinksByFromUrlAsync(
            ctx.Project.ProjectId, 
            fromUrlId);
        
        if (outgoingLinks.Count == 0)
        {
            return; // No links from this page
        }
        
        // Create a finding for each outgoing link
        foreach (var link in outgoingLinks)
        {
            var anchorText = string.IsNullOrWhiteSpace(link.AnchorText) 
                ? "(no text)" 
                : link.AnchorText.Trim();
            var anchorDisplay = anchorText.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            var friendlyLinkType = GetFriendlyLinkType(link.LinkType);
            var issueSummary = anchorText == "(no text)"
                ? $"Internal {friendlyLinkType} without anchor text"
                : $"Internal {friendlyLinkType} with anchor text";
            var description = anchorText == "(no text)"
                ? $"Link graph shows this {friendlyLinkType} from {fromUrlAddress} to {link.ToUrl} lacks anchor text, so the destination may be unclear to visitors and search engines."
                : $"Link graph shows this {friendlyLinkType} from {fromUrlAddress} to {link.ToUrl} using \"{anchorDisplay}\" anchor text so you can verify that the text matches the target page.";
            
            // Create report row with custom columns
            // NOTE: Plugins with registered schemas should create ONLY report rows, not findings
            // This ensures the UI displays custom columns instead of legacy finding columns
            var row = ReportRow.Create()
                .Set("Issue", issueSummary)
                .SetExplanation(description)
                .Set("FromURL", fromUrlAddress)
                .Set("ToURL", link.ToUrl)
                .Set("AnchorText", anchorText)
                .Set("LinkType", link.LinkType)
                .SetSeverity(Severity.Info);
            
            await ctx.Reports.ReportAsync(Key, row, fromUrlId, default);
        }
        
        _logger.LogDebug("Generated {Count} link graph findings for {Url}", 
            outgoingLinks.Count, fromUrlAddress);
    }

    private static string GetFriendlyLinkType(string linkType)
    {
        return linkType switch
        {
            "Hyperlink" => "hyperlink",
            "Image" => "image link",
            "Script" => "script",
            "Stylesheet" => "stylesheet",
            "Other" => "resource",
            _ => linkType.ToLowerInvariant()
        };
    }
}

