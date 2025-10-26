using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.StructuredData;

/// <summary>
/// JSON-LD, Microdata, and Schema.org structured data extraction and validation.
/// </summary>
public class StructuredDataTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;

    public override string Key => "StructuredData";
    public override string DisplayName => "Structured Data";
    public override int Priority => 60; // Run after content analysis

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
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract and validate JSON-LD
            await AnalyzeJsonLdAsync(ctx, doc);

            // Extract and analyze Microdata
            await AnalyzeMicrodataAsync(ctx, doc);

            // Check for missing structured data on important pages
            await CheckMissingStructuredDataAsync(ctx, doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing structured data for {Url}", ctx.Url);
        }
    }

    private async Task AnalyzeJsonLdAsync(UrlContext ctx, HtmlDocument doc)
    {
        // Find all JSON-LD script tags
        var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");

        if (jsonLdNodes == null || jsonLdNodes.Count == 0)
        {
            return;
        }

        List<string> validSchemas = [];

        foreach (var node in jsonLdNodes)
        {
            var jsonContent = node.InnerText?.Trim();
            if (string.IsNullOrEmpty(jsonContent))
            {
                continue;
            }

            try
            {
                // Parse JSON
                var jsonDoc = JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;

                // Extract @type
                string? schemaType = null;
                if (root.TryGetProperty("@type", out var typeElement))
                {
                    schemaType = typeElement.GetString();
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    // Handle array of schema objects
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.TryGetProperty("@type", out var itemType))
                        {
                            schemaType = itemType.GetString();
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(schemaType))
                {
                    validSchemas.Add(schemaType);
                    
                    // Validate based on schema type
                    await ValidateSchemaTypeAsync(ctx, root, schemaType);
                }
            }
            catch (JsonException ex)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "INVALID_JSON_LD",
                    $"JSON-LD syntax error: {ex.Message}",
                    new
                    {
                        url = ctx.Url.ToString(),
                        error = ex.Message,
                        recommendation = "Fix JSON syntax in ld+json script tag"
                    });
            }
        }

        if (validSchemas.Any())
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "JSON_LD_FOUND",
                $"Found {validSchemas.Count} JSON-LD schema(s): {string.Join(", ", validSchemas.Distinct())}",
                new
                {
                    url = ctx.Url.ToString(),
                    schemas = validSchemas.ToArray(),
                    count = validSchemas.Count
                });
        }
    }

    private async Task ValidateSchemaTypeAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        // Validate common Schema.org types
        switch (schemaType)
        {
            case "Article":
            case "NewsArticle":
            case "BlogPosting":
                await ValidateArticleSchemaAsync(ctx, root, schemaType);
                break;

            case "Product":
                await ValidateProductSchemaAsync(ctx, root);
                break;

            case "Organization":
            case "LocalBusiness":
                await ValidateOrganizationSchemaAsync(ctx, root, schemaType);
                break;

            case "BreadcrumbList":
                await ValidateBreadcrumbSchemaAsync(ctx, root);
                break;

            case "FAQPage":
            case "HowTo":
                await ValidateHowToFaqSchemaAsync(ctx, root, schemaType);
                break;

            default:
                _logger.LogDebug("Schema type {SchemaType} found but no specific validation implemented", schemaType);
                break;
        }
    }

    private async Task ValidateArticleSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        List<string> missingProps = [];

        // Required properties for Article
        if (!root.TryGetProperty("headline", out _))
        {
            missingProps.Add("headline");
        }

        if (!root.TryGetProperty("author", out _))
        {
            missingProps.Add("author");
        }

        if (!root.TryGetProperty("datePublished", out _))
        {
            missingProps.Add("datePublished");
        }

        if (!root.TryGetProperty("image", out _))
        {
            missingProps.Add("image");
        }

        if (missingProps.Any())
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INCOMPLETE_ARTICLE_SCHEMA",
                $"{schemaType} schema missing required properties: {string.Join(", ", missingProps)}",
                new
                {
                    url = ctx.Url.ToString(),
                    schemaType,
                    missingProperties = missingProps.ToArray(),
                    recommendation = "Add missing properties for complete Article markup"
                });
        }
    }

    private async Task ValidateProductSchemaAsync(UrlContext ctx, JsonElement root)
    {
        List<string> missingProps = [];

        if (!root.TryGetProperty("name", out _))
        {
            missingProps.Add("name");
        }

        if (!root.TryGetProperty("image", out _))
        {
            missingProps.Add("image");
        }

        if (!root.TryGetProperty("offers", out _) && !root.TryGetProperty("price", out _))
        {
            missingProps.Add("offers or price");
        }

        if (missingProps.Any())
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INCOMPLETE_PRODUCT_SCHEMA",
                $"Product schema missing required properties: {string.Join(", ", missingProps)}",
                new
                {
                    url = ctx.Url.ToString(),
                    missingProperties = missingProps.ToArray(),
                    recommendation = "Add missing properties for complete Product markup"
                });
        }
    }

    private async Task ValidateOrganizationSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        List<string> missingProps = [];

        if (!root.TryGetProperty("name", out _))
        {
            missingProps.Add("name");
        }

        if (!root.TryGetProperty("url", out _))
        {
            missingProps.Add("url");
        }

        if (missingProps.Any())
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INCOMPLETE_ORGANIZATION_SCHEMA",
                $"{schemaType} schema missing required properties: {string.Join(", ", missingProps)}",
                new
                {
                    url = ctx.Url.ToString(),
                    schemaType,
                    missingProperties = missingProps.ToArray(),
                    recommendation = "Add missing properties for complete Organization markup"
                });
        }
    }

    private async Task ValidateBreadcrumbSchemaAsync(UrlContext ctx, JsonElement root)
    {
        if (!root.TryGetProperty("itemListElement", out _))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INVALID_BREADCRUMB_SCHEMA",
                "BreadcrumbList missing itemListElement property",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add itemListElement array with breadcrumb items"
                });
        }
    }

    private async Task ValidateHowToFaqSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        var requiredProp = schemaType == "FAQPage" ? "mainEntity" : "step";

        if (!root.TryGetProperty(requiredProp, out _))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                $"INCOMPLETE_{schemaType.ToUpperInvariant()}_SCHEMA",
                $"{schemaType} schema missing required '{requiredProp}' property",
                new
                {
                    url = ctx.Url.ToString(),
                    schemaType,
                    missingProperty = requiredProp
                });
        }
    }

    private async Task AnalyzeMicrodataAsync(UrlContext ctx, HtmlDocument doc)
    {
        // Find elements with itemscope
        var itemscopeNodes = doc.DocumentNode.SelectNodes("//*[@itemscope]");

        if (itemscopeNodes == null || itemscopeNodes.Count == 0)
        {
            return;
        }

        List<string> itemTypes = [];

        foreach (var node in itemscopeNodes)
        {
            var itemType = node.GetAttributeValue("itemtype", "");
            if (!string.IsNullOrEmpty(itemType))
            {
                itemTypes.Add(itemType);
            }
        }

        if (itemTypes.Any())
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MICRODATA_FOUND",
                $"Found {itemTypes.Count} Microdata item(s)",
                new
                {
                    url = ctx.Url.ToString(),
                    itemTypes = itemTypes.Distinct().ToArray(),
                    count = itemTypes.Count,
                    note = "Consider using JSON-LD instead of Microdata for easier maintenance"
                });
        }
    }

    private async Task CheckMissingStructuredDataAsync(UrlContext ctx, HtmlDocument doc)
    {
        // Check for JSON-LD
        var hasJsonLd = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']") != null;
        
        // Check for Microdata
        var hasMicrodata = doc.DocumentNode.SelectNodes("//*[@itemscope]") != null;

        // If no structured data found on important pages (depth <= 2)
        if (!hasJsonLd && !hasMicrodata && ctx.Metadata.Depth <= 2)
        {
            // Determine likely page type based on URL patterns
            var url = ctx.Url.ToString().ToLowerInvariant();
            string? recommendedSchema = null;

            if (Regex.IsMatch(url, @"/(blog|article|post|news)/"))
            {
                recommendedSchema = "Article or BlogPosting";
            }
            else if (Regex.IsMatch(url, @"/(product|item|shop)/"))
            {
                recommendedSchema = "Product";
            }
            else if (Regex.IsMatch(url, @"/(about|contact|company)/"))
            {
                recommendedSchema = "Organization";
            }

            if (recommendedSchema != null)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "MISSING_STRUCTURED_DATA",
                    $"Page appears to be a {recommendedSchema} page but has no structured data",
                    new
                    {
                        url = ctx.Url.ToString(),
                        recommendedSchema,
                        recommendation = "Add JSON-LD structured data to improve search appearance"
                    });
            }
        }
    }
}

