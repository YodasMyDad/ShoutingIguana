using System.Text.RegularExpressions;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.Plugins.CustomExtraction.Models;

namespace ShoutingIguana.Plugins.CustomExtraction;

/// <summary>
/// User-defined data extraction using CSS selectors, XPath, and Regex patterns.
/// </summary>
public class CustomExtractionTask(ILogger logger, IServiceProvider serviceProvider) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    
    // Cache rules per project to avoid database query on every URL
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<ExtractionRule>> _rulesCache = new();

    public override string Key => "CustomExtraction";
    public override string DisplayName => "Custom Extraction";
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

    private async Task ApplyExtractionRuleAsync(UrlContext ctx, HtmlDocument doc, ExtractionRule rule)
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
                // Report extracted data as findings
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    $"CUSTOM_DATA_EXTRACTED_{rule.Name.ToUpperInvariant()}",
                    $"Extracted {extractedValues.Count} value(s) for '{rule.Name}'",
                    new
                    {
                        url = ctx.Url.ToString(),
                        ruleName = rule.Name,
                        fieldName = rule.FieldName,
                        selectorType = rule.SelectorType.ToString(),
                        selector = rule.Selector,
                        extractedValues = extractedValues.Take(10).ToArray(), // Limit to first 10
                        totalCount = extractedValues.Count
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply extraction rule '{RuleName}' for {Url}", rule.Name, ctx.Url);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "EXTRACTION_RULE_ERROR",
                $"Failed to apply extraction rule '{rule.Name}': {ex.Message}",
                new
                {
                    url = ctx.Url.ToString(),
                    ruleName = rule.Name,
                    error = ex.Message
                });
        }
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
    private async Task<List<ExtractionRule>> GetOrLoadRulesAsync(int projectId)
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
    private async Task<List<ExtractionRule>> LoadRulesAsync(int projectId)
    {
        try
        {
            // Use Microsoft.Extensions.DependencyInjection to resolve the service
            using var scope = _serviceProvider.CreateScope();
            var serviceType = Type.GetType("ShoutingIguana.Core.Services.ICustomExtractionService, ShoutingIguana.Core");
            if (serviceType == null)
            {
                _logger.LogWarning("Could not load ICustomExtractionService type");
                return new List<ExtractionRule>();
            }
            
            var customExtractionService = scope.ServiceProvider.GetService(serviceType);
            
            if (customExtractionService == null)
            {
                _logger.LogWarning("ICustomExtractionService not available, skipping custom extraction");
                return new List<ExtractionRule>();
            }

            // Use reflection to call GetRulesByProjectIdAsync
            var method = customExtractionService.GetType().GetMethod("GetRulesByProjectIdAsync");
            if (method == null)
            {
                _logger.LogWarning("GetRulesByProjectIdAsync method not found");
                return new List<ExtractionRule>();
            }

            var taskResult = method.Invoke(customExtractionService, new object[] { projectId });
            if (taskResult is not Task task)
            {
                _logger.LogWarning("GetRulesByProjectIdAsync did not return a Task");
                return new List<ExtractionRule>();
            }
            
            await task.ConfigureAwait(false);

            // Get the result
            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty == null)
            {
                _logger.LogWarning("Task.Result property not found");
                return new List<ExtractionRule>();
            }
            
            var resultValue = resultProperty.GetValue(task);
            if (resultValue is not System.Collections.IEnumerable dbRules)
            {
                _logger.LogWarning("Result is not enumerable");
                return new List<ExtractionRule>();
            }

            // Convert database models to plugin models
            var rules = new List<ExtractionRule>();
            foreach (var dbRule in dbRules)
            {
                try
                {
                    var ruleType = dbRule.GetType();
                    var name = ruleType.GetProperty("Name")?.GetValue(dbRule) as string;
                    var fieldName = ruleType.GetProperty("FieldName")?.GetValue(dbRule) as string;
                    var selectorTypeObj = ruleType.GetProperty("SelectorType")?.GetValue(dbRule);
                    var selector = ruleType.GetProperty("Selector")?.GetValue(dbRule) as string;
                    var isEnabledObj = ruleType.GetProperty("IsEnabled")?.GetValue(dbRule);

                    if (name == null || fieldName == null || selectorTypeObj == null || 
                        selector == null || isEnabledObj == null)
                    {
                        _logger.LogWarning("Rule has null properties, skipping");
                        continue;
                    }

                    var selectorType = (int)selectorTypeObj;
                    var isEnabled = (bool)isEnabledObj;

                    if (isEnabled)
                    {
                        rules.Add(new ExtractionRule
                        {
                            Name = name,
                            FieldName = fieldName,
                            SelectorType = (SelectorType)selectorType,
                            Selector = selector
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error converting rule, skipping");
                }
            }

            _logger.LogDebug("Loaded {Count} extraction rules for project {ProjectId}", rules.Count, projectId);
            return rules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading extraction rules for project {ProjectId}", projectId);
            return new List<ExtractionRule>();
        }
    }

}

