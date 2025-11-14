using System;
using System.Text.RegularExpressions;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.CustomExtraction;

/// <summary>
/// User-defined data extraction using CSS selectors, XPath, and Regex patterns.
/// </summary>
public class CustomExtractionTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    
    // Cache rules per project to avoid database query on every URL
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<CustomExtractionRuleInfo>> _rulesCache = new();

    public override string Key => "CustomExtraction";
    public override string DisplayName => "Custom Extraction";
    public override string Description => "Extracts custom data using CSS selectors, XPath, and regex patterns you define";
    public override int Priority => 70; // Run after most other analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful pages (skip 4xx, 5xx errors)
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Load extraction rules from database (with caching per project)
            var rules = await GetOrLoadRulesAsync(ctx.Project.ProjectId);
            
            if (rules.Count == 0)
            {
                // No rules defined, nothing to extract
                return;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Apply each extraction rule
            foreach (var rule in rules)
            {
                await ApplyExtractionRuleAsync(ctx, doc, rule);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing custom extraction for {Url}", ctx.Url);
        }
    }

    private async Task ApplyExtractionRuleAsync(UrlContext ctx, HtmlDocument doc, CustomExtractionRuleInfo rule)
    {
        try
        {
            var extractedValues = rule.SelectorType switch
            {
                SelectorType.Css => ExtractByCssSelector(doc, rule.Selector),
                SelectorType.XPath => ExtractByXPath(doc, rule.Selector),
                SelectorType.Regex => ExtractByRegex(ctx.RenderedHtml!, rule.Selector),
                _ => new List<string>()
            };

            if (extractedValues.Any())
            {
                var formattedValues = FormatExtractedValues(extractedValues);
                
                var row = ReportRow.Create()
                    .SetPage(ctx.Url)
                    .Set("RuleName", rule.Name)
                    .Set("ExtractedValue", formattedValues)
                    .Set("ExtractedValuesRaw", extractedValues.ToArray())
                    .Set("Selector", rule.Selector)
                    .Set("Count", extractedValues.Count)
                    .SetSeverity(Severity.Info);
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply extraction rule '{RuleName}' for {Url}", rule.Name, ctx.Url);
            
            var row = ReportRow.Create()
                .SetPage(ctx.Url)
                .Set("RuleName", rule.Name)
                .Set("ExtractedValue", "ERROR")
                .Set("Selector", rule.Selector)
                .Set("Count", 0)
                .SetSeverity(Severity.Warning);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private static string FormatExtractedValues(List<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        // Trim whitespace and collapse newline characters so the summary fits nicely in the grid/export
        var cleaned = values
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim());

        return string.Join(Environment.NewLine, cleaned);
    }

    private List<string> ExtractByCssSelector(HtmlDocument doc, string cssSelector)
    {
        List<string> results = [];

        try
        {
            // Use Fizzler for full CSS3 selector support
            var nodes = doc.DocumentNode.QuerySelectorAll(cssSelector);
            
            foreach (var node in nodes)
            {
                var value = node.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    results.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute CSS selector: {Selector}. Error: {Message}", cssSelector, ex.Message);
        }

        return results;
    }

    private List<string> ExtractByXPath(HtmlDocument doc, string xpath)
    {
        List<string> results = [];

        try
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var value = node.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        results.Add(value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute XPath: {XPath}", xpath);
        }

        return results;
    }

    private List<string> ExtractByRegex(string html, string pattern)
    {
        List<string> results = [];

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var matches = regex.Matches(html);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    // Use first capture group
                    results.Add(match.Groups[1].Value.Trim());
                }
                else
                {
                    results.Add(match.Value.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute regex: {Pattern}", pattern);
        }

        return results;
    }

    /// <summary>
    /// Gets rules from cache or loads from database if not cached.
    /// </summary>
    private async Task<List<CustomExtractionRuleInfo>> GetOrLoadRulesAsync(int projectId)
    {
        // Check cache first (avoid database query for every URL)
        if (_rulesCache.TryGetValue(projectId, out var cachedRules))
        {
            return cachedRules;
        }
        
        // Load from database and cache
        var rules = await LoadRulesAsync(projectId);
        _rulesCache[projectId] = rules;
        return rules;
    }
    
    /// <summary>
    /// Loads extraction rules from the database for the specified project.
    /// </summary>
    private async Task<List<CustomExtractionRuleInfo>> LoadRulesAsync(int projectId)
    {
        try
        {
            var rules = await _repositoryAccessor.GetCustomExtractionRulesAsync(projectId);
            var enabledRules = rules.Where(r => r.IsEnabled).ToList();
            _logger.LogDebug("Loaded {Count} enabled extraction rules for project {ProjectId}", enabledRules.Count, projectId);
            return enabledRules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading extraction rules for project {ProjectId}", projectId);
            return new List<CustomExtractionRuleInfo>();
        }
    }
    
    /// <summary>
    /// Cleanup per-project cache when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        _rulesCache.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up custom extraction rules cache for project {ProjectId}", projectId);
    }

}

