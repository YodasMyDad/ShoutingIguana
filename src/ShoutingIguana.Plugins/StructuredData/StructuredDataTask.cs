using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
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
    public override string Description => "Extracts and validates JSON-LD, Microdata, and Schema.org markup";
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem("JSON-LD syntax error")
                    .AddItem($"Error: {ex.Message}")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Fix JSON syntax in ld+json script tag")
                        .AddItem("Use JSON validator to check structure")
                        .AddItem("Common issues: trailing commas, unescaped quotes")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("error", ex.Message)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "INVALID_JSON_LD",
                    $"JSON-LD syntax error: {ex.Message}",
                    details);
            }
        }

        if (validSchemas.Any())
        {
            var distinctSchemas = validSchemas.Distinct().ToList();
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Found {validSchemas.Count} JSON-LD schema(s)");
            
            builder.BeginNested("📋 Schema types");
            foreach (var schema in distinctSchemas)
            {
                builder.AddItem(schema);
            }
            
            builder.AddItem("✅ Structured data helps search engines understand your content")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("schemas", validSchemas.ToArray())
                .WithTechnicalMetadata("count", validSchemas.Count);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "JSON_LD_FOUND",
                $"Found {validSchemas.Count} JSON-LD schema(s): {string.Join(", ", distinctSchemas)}",
                builder.Build());
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

            case "VideoObject":
                await ValidateVideoObjectSchemaAsync(ctx, root);
                break;

            case "Review":
                await ValidateReviewSchemaAsync(ctx, root);
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
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Schema type: {schemaType}");
            
            builder.BeginNested("❌ Missing required properties");
            foreach (var prop in missingProps)
            {
                builder.AddItem(prop);
            }
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Add missing properties for complete Article markup")
                .AddItem("Complete schemas qualify for rich results");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("schemaType", schemaType)
                .WithTechnicalMetadata("missingProperties", missingProps.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INCOMPLETE_ARTICLE_SCHEMA",
                $"{schemaType} schema missing required properties: {string.Join(", ", missingProps)}",
                builder.Build());
        }
    }

    private async Task ValidateProductSchemaAsync(UrlContext ctx, JsonElement root)
    {
        List<string> missingProps = [];
        List<string> warnings = [];

        // Required properties
        if (!root.TryGetProperty("name", out _))
        {
            missingProps.Add("name");
        }

        if (!root.TryGetProperty("image", out _))
        {
            missingProps.Add("image");
        }

        // Validate offers/price
        bool hasOffers = root.TryGetProperty("offers", out var offersElement);
        bool hasPrice = root.TryGetProperty("price", out _);

        if (!hasOffers && !hasPrice)
        {
            missingProps.Add("offers or price");
        }
        else if (hasOffers)
        {
            // Validate offers structure
            if (offersElement.ValueKind == JsonValueKind.Object)
            {
                // Single offer
                await ValidateOfferAsync(ctx, offersElement, warnings);
            }
            else if (offersElement.ValueKind == JsonValueKind.Array)
            {
                // Multiple offers
                foreach (var offer in offersElement.EnumerateArray())
                {
                    await ValidateOfferAsync(ctx, offer, warnings);
                }
            }
        }

        // Check for reviews/aggregateRating
        if (root.TryGetProperty("aggregateRating", out var ratingElement))
        {
            await ValidateAggregateRatingAsync(ctx, ratingElement, warnings);
        }

        // Check for brand (recommended)
        if (!root.TryGetProperty("brand", out _))
        {
            warnings.Add("Missing 'brand' property (recommended for Product schema)");
        }

        // Check for description (recommended)
        if (!root.TryGetProperty("description", out _))
        {
            warnings.Add("Missing 'description' property (recommended for Product schema)");
        }

        if (missingProps.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem("Product schema incomplete");
            
            builder.BeginNested("❌ Missing required properties");
            foreach (var prop in missingProps)
            {
                builder.AddItem(prop);
            }
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Add missing properties per Google guidelines")
                .AddItem("Complete Product schema qualifies for rich results");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("missingProperties", missingProps.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "INCOMPLETE_PRODUCT_SCHEMA",
                $"Product schema missing required properties: {string.Join(", ", missingProps)}",
                builder.Build());
        }

        if (warnings.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Product schema has {warnings.Count} recommended improvements");
            
            builder.BeginNested("⚠️ Recommendations");
            foreach (var warning in warnings)
            {
                builder.AddItem(warning);
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("warnings", warnings.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "PRODUCT_SCHEMA_RECOMMENDATIONS",
                $"Product schema has {warnings.Count} recommended improvements",
                builder.Build());
        }
    }

    private async Task ValidateOfferAsync(UrlContext ctx, JsonElement offer, List<string> warnings)
    {
        // Validate price
        if (offer.TryGetProperty("price", out var priceElement))
        {
            var priceString = priceElement.ToString();
            // Check for currency symbols in price (should be numeric only)
            if (priceString.Contains("$") || priceString.Contains("€") || priceString.Contains("£"))
            {
                warnings.Add($"Price contains currency symbol: '{priceString}' (should be numeric only)");
            }
        }

        // Validate priceCurrency (ISO 4217)
        if (offer.TryGetProperty("priceCurrency", out var currencyElement))
        {
            var currency = currencyElement.GetString();
            var validCurrencies = new[] { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CNY", "INR", "BRL", "MXN" };
            if (!string.IsNullOrEmpty(currency) && currency.Length != 3)
            {
                warnings.Add($"priceCurrency '{currency}' should be ISO 4217 3-letter code");
            }
        }
        else
        {
            warnings.Add("Missing 'priceCurrency' in offers (required for rich results)");
        }

        // Validate availability
        if (offer.TryGetProperty("availability", out var availElement))
        {
            var availability = availElement.GetString();
            var validValues = new[] {
                "https://schema.org/InStock",
                "https://schema.org/OutOfStock",
                "https://schema.org/PreOrder",
                "https://schema.org/PreSale",
                "https://schema.org/SoldOut",
                "https://schema.org/Discontinued",
                "https://schema.org/LimitedAvailability"
            };

            if (!string.IsNullOrEmpty(availability) && !validValues.Any(v => availability.Contains(v)))
            {
                warnings.Add($"Availability '{availability}' should use schema.org URL format");
            }
        }
        
        await Task.CompletedTask;
    }

    private async Task ValidateAggregateRatingAsync(UrlContext ctx, JsonElement rating, List<string> warnings)
    {
        // Validate ratingValue
        if (rating.TryGetProperty("ratingValue", out var ratingValueElement))
        {
            if (ratingValueElement.TryGetDouble(out var ratingValue))
            {
                if (ratingValue < 1 || ratingValue > 5)
                {
                    warnings.Add($"ratingValue {ratingValue} outside typical range (1-5)");
                }
                
                // Detect suspiciously perfect ratings
                if (ratingValue >= 4.9 && rating.TryGetProperty("reviewCount", out var reviewCountElement))
                {
                    if (reviewCountElement.TryGetInt32(out var reviewCount) && reviewCount > 50)
                    {
                        warnings.Add($"Suspiciously high rating ({ratingValue}) with many reviews ({reviewCount}) - may appear fake to users");
                    }
                }
            }
        }

        // Validate reviewCount
        if (rating.TryGetProperty("reviewCount", out var countElement))
        {
            if (countElement.TryGetInt32(out var count) && count <= 0)
            {
                warnings.Add("reviewCount should be greater than 0");
            }
        }
        else
        {
            warnings.Add("Missing 'reviewCount' in aggregateRating (required for rich results)");
        }

        // Validate bestRating (if present)
        if (rating.TryGetProperty("bestRating", out var bestElement))
        {
            if (bestElement.TryGetInt32(out var best) && best != 5)
            {
                // Non-standard scale, should also have worstRating
                if (!rating.TryGetProperty("worstRating", out _))
                {
                    warnings.Add("bestRating is non-standard, should also include worstRating");
                }
            }
        }
        
        await Task.CompletedTask;
    }

    private async Task ValidateVideoObjectSchemaAsync(UrlContext ctx, JsonElement root)
    {
        List<string> missingProps = [];
        List<string> warnings = [];

        // Required properties for VideoObject
        if (!root.TryGetProperty("name", out _))
        {
            missingProps.Add("name");
        }

        if (!root.TryGetProperty("description", out _))
        {
            missingProps.Add("description");
        }

        if (!root.TryGetProperty("thumbnailUrl", out var thumbnailElement))
        {
            missingProps.Add("thumbnailUrl");
        }
        else
        {
            // Validate thumbnail is an array or string
            if (thumbnailElement.ValueKind != JsonValueKind.String && thumbnailElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("thumbnailUrl should be a URL string or array of URLs");
            }
        }

        if (!root.TryGetProperty("uploadDate", out var uploadDateElement))
        {
            missingProps.Add("uploadDate");
        }
        else
        {
            // Validate date format (ISO 8601)
            var uploadDate = uploadDateElement.GetString();
            if (!string.IsNullOrEmpty(uploadDate) && !DateTime.TryParse(uploadDate, out _))
            {
                warnings.Add($"uploadDate '{uploadDate}' should be ISO 8601 format (YYYY-MM-DD)");
            }
        }

        // Check for contentUrl or embedUrl (at least one required)
        bool hasContentUrl = root.TryGetProperty("contentUrl", out _);
        bool hasEmbedUrl = root.TryGetProperty("embedUrl", out _);

        if (!hasContentUrl && !hasEmbedUrl)
        {
            missingProps.Add("contentUrl or embedUrl");
        }

        // Check for duration (recommended)
        if (root.TryGetProperty("duration", out var durationElement))
        {
            var duration = durationElement.GetString();
            // Validate ISO 8601 duration format (PT1H30M)
            if (!string.IsNullOrEmpty(duration) && !duration.StartsWith("PT"))
            {
                warnings.Add($"duration '{duration}' should be ISO 8601 format (e.g., PT1H30M for 1 hour 30 minutes)");
            }
        }
        else
        {
            warnings.Add("Missing 'duration' property (recommended for VideoObject)");
        }

        // Check for interactionStatistic (view count - recommended)
        if (!root.TryGetProperty("interactionStatistic", out _))
        {
            warnings.Add("Missing 'interactionStatistic' (view count recommended for better visibility)");
        }

        if (missingProps.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem("VideoObject schema incomplete");
            
            builder.BeginNested("❌ Missing required properties");
            foreach (var prop in missingProps)
            {
                builder.AddItem(prop);
            }
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Add missing properties for Video rich results")
                .AddItem("Complete VideoObject appears in Google Video search");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("missingProperties", missingProps.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "INCOMPLETE_VIDEO_SCHEMA",
                $"VideoObject schema missing required properties: {string.Join(", ", missingProps)}",
                builder.Build());
        }

        if (warnings.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"VideoObject schema: {warnings.Count} improvements");
            
            builder.BeginNested("⚠️ Recommendations");
            foreach (var warning in warnings)
            {
                builder.AddItem(warning);
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("warnings", warnings.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "VIDEO_SCHEMA_RECOMMENDATIONS",
                $"VideoObject schema has {warnings.Count} recommended improvements",
                builder.Build());
        }
    }

    private async Task ValidateReviewSchemaAsync(UrlContext ctx, JsonElement root)
    {
        List<string> missingProps = [];
        List<string> warnings = [];

        // Required properties for Review
        if (!root.TryGetProperty("itemReviewed", out _))
        {
            missingProps.Add("itemReviewed");
        }

        if (!root.TryGetProperty("reviewRating", out var reviewRatingElement))
        {
            missingProps.Add("reviewRating");
        }
        else
        {
            // Validate rating structure
            if (reviewRatingElement.TryGetProperty("ratingValue", out var ratingValueElement))
            {
                if (ratingValueElement.TryGetDouble(out var ratingValue))
                {
                    if (ratingValue < 1 || ratingValue > 5)
                    {
                        warnings.Add($"reviewRating ratingValue {ratingValue} outside typical range (1-5)");
                    }
                }
            }
            else
            {
                warnings.Add("reviewRating missing 'ratingValue' property");
            }
        }

        if (!root.TryGetProperty("author", out _))
        {
            missingProps.Add("author");
        }

        // Check for reviewBody (recommended)
        if (!root.TryGetProperty("reviewBody", out var reviewBodyElement))
        {
            warnings.Add("Missing 'reviewBody' (recommended for Review schema)");
        }
        else
        {
            var reviewBody = reviewBodyElement.GetString();
            if (!string.IsNullOrEmpty(reviewBody) && reviewBody.Length < 50)
            {
                warnings.Add($"reviewBody is very short ({reviewBody.Length} chars) - detailed reviews perform better");
            }
        }

        // Check for datePublished (recommended)
        if (!root.TryGetProperty("datePublished", out _))
        {
            warnings.Add("Missing 'datePublished' (recommended for Review schema)");
        }

        if (missingProps.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem("Review schema incomplete");
            
            builder.BeginNested("❌ Missing required properties");
            foreach (var prop in missingProps)
            {
                builder.AddItem(prop);
            }
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Add missing properties to qualify for rich results")
                .AddItem("Complete Review schema shows star ratings in search");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("missingProperties", missingProps.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "INCOMPLETE_REVIEW_SCHEMA",
                $"Review schema missing required properties: {string.Join(", ", missingProps)}",
                builder.Build());
        }

        if (warnings.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Review schema: {warnings.Count} improvements");
            
            builder.BeginNested("⚠️ Recommendations");
            foreach (var warning in warnings)
            {
                builder.AddItem(warning);
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("warnings", warnings.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "REVIEW_SCHEMA_RECOMMENDATIONS",
                $"Review schema has {warnings.Count} recommended improvements",
                builder.Build());
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
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"{schemaType} schema incomplete");
            
            builder.BeginNested("❌ Missing required properties");
            foreach (var prop in missingProps)
            {
                builder.AddItem(prop);
            }
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("schemaType", schemaType)
                .WithTechnicalMetadata("missingProperties", missingProps.ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INCOMPLETE_ORGANIZATION_SCHEMA",
                $"{schemaType} schema missing required properties: {string.Join(", ", missingProps)}",
                builder.Build());
        }
    }

    private async Task ValidateBreadcrumbSchemaAsync(UrlContext ctx, JsonElement root)
    {
        if (!root.TryGetProperty("itemListElement", out _))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("BreadcrumbList schema incomplete")
                .AddItem("❌ Missing itemListElement property")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add itemListElement array with breadcrumb items")
                    .AddItem("Each item should have name and position")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "INVALID_BREADCRUMB_SCHEMA",
                "BreadcrumbList missing itemListElement property",
                details);
        }
    }

    private async Task ValidateHowToFaqSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        var requiredProp = schemaType == "FAQPage" ? "mainEntity" : "step";

        if (!root.TryGetProperty(requiredProp, out _))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"{schemaType} schema incomplete")
                .AddItem($"❌ Missing required '{requiredProp}' property")
                .BeginNested("💡 Recommendations")
                    .AddItem($"Add {requiredProp} array to the schema")
                    .AddItem("This property is required for rich results")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("schemaType", schemaType)
                .WithTechnicalMetadata("missingProperty", requiredProp)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                $"INCOMPLETE_{schemaType.ToUpperInvariant()}_SCHEMA",
                $"{schemaType} schema missing required '{requiredProp}' property",
                details);
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
            var distinctTypes = itemTypes.Distinct().ToList();
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Found {itemTypes.Count} Microdata item(s)");
            
            builder.BeginNested("📋 Item types");
            foreach (var type in distinctTypes)
            {
                builder.AddItem(type);
            }
            
            builder.AddItem("ℹ️ Consider using JSON-LD instead for easier maintenance")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("itemTypes", distinctTypes.ToArray())
                .WithTechnicalMetadata("count", itemTypes.Count);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MICRODATA_FOUND",
                $"Found {itemTypes.Count} Microdata item(s)",
                builder.Build());
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Page appears to be: {recommendedSchema}")
                    .AddItem("❌ No structured data found")
                    .AddItem($"Page depth: {ctx.Metadata.Depth} (important page)")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Add JSON-LD structured data")
                        .AddItem("Structured data improves search appearance")
                        .AddItem("Can qualify for rich results and enhanced listings")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("recommendedSchema", recommendedSchema)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "MISSING_STRUCTURED_DATA",
                    $"Page appears to be a {recommendedSchema} page but has no structured data",
                    details);
            }
        }
    }
}


