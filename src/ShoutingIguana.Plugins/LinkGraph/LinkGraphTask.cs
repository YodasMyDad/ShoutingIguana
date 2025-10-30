using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.LinkGraph;

/// <summary>
/// Generates findings for each internal link to visualize the link graph.
/// Each link becomes an Info-level finding showing FROM URL -> TO URL with anchor text.
/// </summary>
public class LinkGraphTask : UrlTaskBase
{
    private readonly ILogger _logger;
    private readonly IRepositoryAccessor _repositoryAccessor;

    public override string Key => "LinkGraph";
    public override string DisplayName => "Link Graph";
    public override string Description => "Internal linking structure visualization";
    public override int Priority => 1000; // Run after other analysis tasks

    public LinkGraphTask(ILogger logger, IRepositoryAccessor repositoryAccessor)
    {
        _logger = logger;
        _repositoryAccessor = repositoryAccessor;
    }

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
                : link.AnchorText;
            
            var message = $"Links to: {link.ToUrl}";
            
            var details = FindingDetailsBuilder.Create()
                .AddItem($"From URL: {fromUrlAddress}")
                .AddItem($"To URL: {link.ToUrl}")
                .AddItem($"Anchor Text: \"{anchorText}\"")
                .AddItem($"Link Type: {link.LinkType}")
                .WithTechnicalMetadata("fromUrl", fromUrlAddress)
                .WithTechnicalMetadata("toUrl", link.ToUrl)
                .WithTechnicalMetadata("anchorText", anchorText)
                .WithTechnicalMetadata("linkType", link.LinkType)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "LINK_GRAPH",
                message,
                details);
        }
        
        _logger.LogDebug("Generated {Count} link graph findings for {Url}", 
            outgoingLinks.Count, fromUrlAddress);
    }
}

