using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.StructuredData;

/// <summary>
/// JSON-LD, Microdata, and Schema.org structured data extraction and validation.
/// </summary>
public class StructuredDataTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    
    // Track LocalBusiness NAP (Name, Address, Phone) across pages for consistency checking
    private static readonly ConcurrentDictionary<int, ConcurrentBag<NAPInfo>> NAPByProject = new();
    
    // Track if NAP inconsistency has been reported for this project (report only once)
    private static readonly ConcurrentDictionary<int, bool> NAPInconsistencyReportedByProject = new();

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
            
            // Check for author markup on articles
            await CheckAuthorMarkupAsync(ctx, doc);
            
            // Scan for contact information
            await ScanContactInformationAsync(ctx, doc);
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

                // Extract @type - JSON-LD can be either a single object or an array of objects
                // Check ValueKind FIRST to avoid InvalidOperationException
                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Handle array of schema objects
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.TryGetProperty("@type", out var itemType))
                        {
                            var schemaType = itemType.GetString();
                            if (!string.IsNullOrEmpty(schemaType))
                            {
                                validSchemas.Add(schemaType);
                                // Validate based on schema type
                                await ValidateSchemaTypeAsync(ctx, item, schemaType);
                            }
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    // Handle single object
                    if (root.TryGetProperty("@type", out var typeElement))
                    {
                        var schemaType = typeElement.GetString();
                        if (!string.IsNullOrEmpty(schemaType))
                        {
                            validSchemas.Add(schemaType);
                            // Validate based on schema type
                            await ValidateSchemaTypeAsync(ctx, root, schemaType);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                var jsonPath = string.IsNullOrEmpty(ex.Path) ? "JSON-LD" : ex.Path;
                var errorDescription = $"Invalid JSON-LD at {jsonPath}: {ex.Message}. Fix the syntax so the structured data can be parsed.";
                var rowError = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "JSON-LD")
                    .Set("Issue", $"JSON Syntax Error: {ex.Message}")
                    .Set("Description", errorDescription)
                    .Set("Property", jsonPath)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, rowError, ctx.Metadata.UrlId, default);
            }
        }

        if (validSchemas.Any())
        {
            var distinctSchemas = validSchemas.Distinct().ToList();
            var schemaList = string.Join(", ", distinctSchemas);
            
            var foundDescription = $"Detected {validSchemas.Count} JSON-LD schema definition(s) ({schemaList}); these help search engines understand the page.";
            var rowFound = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", schemaList)
                .Set("Issue", $"JSON-LD Found ({validSchemas.Count} schemas)")
                .Set("Description", foundDescription)
                .Set("Property", schemaList)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, rowFound, ctx.Metadata.UrlId, default);
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
            var missingList = string.Join(", ", missingProps);
            var articleDescription = $"Article schema missing required property(ies) ({missingList}). Add them so the article metadata (headline, author, date, image) is complete.";
            var rowArticle = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", schemaType)
                .Set("Issue", "Incomplete Article Schema")
                .Set("Description", articleDescription)
                .Set("Property", missingList)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowArticle, ctx.Metadata.UrlId, default);
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
        bool hasAggregateRating = root.TryGetProperty("aggregateRating", out var ratingElement);
        if (hasAggregateRating)
        {
            await ValidateAggregateRatingAsync(ctx, ratingElement, warnings);
        }
        else
        {
            // Missing aggregate rating - note the CTR opportunity
            warnings.Add("Missing 'aggregateRating' - star ratings in SERPs = massive CTR boost");
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
            var missingList = string.Join(", ", missingProps);
            var productMissingDescription = $"Product schema missing required property(ies) ({missingList}); include them so the product can appear in rich results.";
            var rowProdMissing = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Product")
                .Set("Issue", "Incomplete Product Schema")
                .Set("Description", productMissingDescription)
                .Set("Property", missingList)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowProdMissing, ctx.Metadata.UrlId, default);
        }

        if (warnings.Any())
        {
            var warningPreview = string.Join("; ", warnings.Take(2));
            var warningDescription = $"Product schema has {warnings.Count} optimization recommendation(s) (e.g., {warningPreview}); address them to improve eligibility for rich snippets.";
            var rowProdWarn = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Product")
                .Set("Issue", $"Product Recommendations ({warnings.Count})")
                .Set("Description", warningDescription)
                .Set("Property", hasAggregateRating ? "" : "aggregateRating")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowProdWarn, ctx.Metadata.UrlId, default);
        }
        else if (hasAggregateRating)
        {
            var completeDescription = "Product schema includes aggregateRating, so star ratings may appear in search results; keep rating data accurate.";
            var rowComplete = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Product")
                .Set("Issue", "Complete with Star Ratings")
                .Set("Description", completeDescription)
                .Set("Property", "aggregateRating")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, rowComplete, ctx.Metadata.UrlId, default);
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
            if (TryGetDoubleValue(ratingValueElement, out var ratingValue))
            {
                if (ratingValue < 1 || ratingValue > 5)
                {
                    warnings.Add($"ratingValue {ratingValue} outside typical range (1-5)");
                }
                
                // Detect suspiciously perfect ratings
                if (ratingValue >= 4.9 && rating.TryGetProperty("reviewCount", out var reviewCountElement))
                {
                    if (TryGetInt32Value(reviewCountElement, out var reviewCount) && reviewCount > 50)
                    {
                        warnings.Add($"Suspiciously high rating ({ratingValue}) with many reviews ({reviewCount}) - may appear fake to users");
                    }
                }
            }
        }

        // Validate reviewCount
        if (rating.TryGetProperty("reviewCount", out var countElement))
        {
            if (TryGetInt32Value(countElement, out var count) && count <= 0)
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
            if (TryGetInt32Value(bestElement, out var best) && best != 5)
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
            var missingList = string.Join(", ", missingProps);
            var videoMissingDescription = $"VideoObject schema missing required property(ies) ({missingList}); add them so video rich results can recognize this content.";
            var rowVidMissing = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "VideoObject")
                .Set("Issue", "Incomplete Video Schema")
                .Set("Description", videoMissingDescription)
                .Set("Property", missingList)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowVidMissing, ctx.Metadata.UrlId, default);
        }

        if (warnings.Any())
        {
            var warningPreview = string.Join("; ", warnings.Take(2));
            var videoWarningDescription = $"Video schema has {warnings.Count} recommendations (e.g., {warningPreview}); fix them so your video markup meets schema.org expectations.";
            var rowVideo = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "VideoObject")
                .Set("Issue", $"Video Recommendations ({warnings.Count})")
                .Set("Description", videoWarningDescription)
                .Set("Property", string.Join(", ", warnings.Take(2)))
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowVideo, ctx.Metadata.UrlId, default);
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
                if (TryGetDoubleValue(ratingValueElement, out var ratingValue))
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
            var missingList = string.Join(", ", missingProps);
            var reviewMissingDescription = $"Review schema missing required property(ies) ({missingList}); add them so reviews can appear in search results.";
            var rowRevMissing = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Review")
                .Set("Issue", "Incomplete Review Schema")
                .Set("Description", reviewMissingDescription)
                .Set("Property", missingList)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, rowRevMissing, ctx.Metadata.UrlId, default);
        }

        if (warnings.Any())
        {
            var warningPreview = string.Join("; ", warnings.Take(2));
            var reviewWarningDescription = $"Review schema has {warnings.Count} recommendations (e.g., {warningPreview}); improve them so rating snippets stay trustworthy.";
            var rowRevWarn = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Review")
                .Set("Issue", $"Review Recommendations ({warnings.Count})")
                .Set("Description", reviewWarningDescription)
                .Set("Property", string.Join(", ", warnings.Take(2)))
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowRevWarn, ctx.Metadata.UrlId, default);
        }
        else if (!missingProps.Any())
        {
            var reviewCompleteDescription = "Review schema has all required fields, so reviews can show rating snippets; keep the facts accurate.";
            var rowRevComplete = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Review")
                .Set("Issue", "Review Schema Complete")
                .Set("Description", reviewCompleteDescription)
                .Set("Property", "Review fields complete")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, rowRevComplete, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateOrganizationSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        List<string> missingProps = [];
        List<string> warnings = [];

        if (!root.TryGetProperty("name", out _))
        {
            missingProps.Add("name");
        }

        if (!root.TryGetProperty("url", out _))
        {
            missingProps.Add("url");
        }

        // Enhanced validation for LocalBusiness (critical for local pack)
        if (schemaType == "LocalBusiness")
        {
            // Extract NAP info for consistency checking
            string? businessName = null;
            string? phone = null;
            string? address = null;
            
            if (root.TryGetProperty("name", out var nameElement))
            {
                businessName = nameElement.GetString();
            }
            
            if (root.TryGetProperty("telephone", out var phoneElement))
            {
                phone = phoneElement.GetString();
            }
            
            // Address is critical for local pack visibility
            if (!root.TryGetProperty("address", out var addressElement))
            {
                missingProps.Add("address (REQUIRED for local pack)");
            }
            else
            {
                // Validate address structure
                if (addressElement.ValueKind == JsonValueKind.Object)
                {
                    // Check for PostalAddress type
                    if (!addressElement.TryGetProperty("@type", out var addressType) || 
                        addressType.GetString() != "PostalAddress")
                    {
                        warnings.Add("address should have @type: PostalAddress");
                    }
                    
                    // Build full address string for NAP tracking
                    var addressParts = new List<string>();
                    
                    // Check required address fields
                    if (!addressElement.TryGetProperty("streetAddress", out var streetElement))
                    {
                        warnings.Add("address missing 'streetAddress' property");
                    }
                    else
                    {
                        addressParts.Add(streetElement.GetString() ?? "");
                    }
                    
                    if (!addressElement.TryGetProperty("addressLocality", out var localityElement))
                    {
                        warnings.Add("address missing 'addressLocality' (city) property");
                    }
                    else
                    {
                        addressParts.Add(localityElement.GetString() ?? "");
                    }
                    
                    if (!addressElement.TryGetProperty("addressRegion", out var regionElement))
                    {
                        warnings.Add("address missing 'addressRegion' (state/province) property");
                    }
                    else
                    {
                        addressParts.Add(regionElement.GetString() ?? "");
                    }
                    
                    if (!addressElement.TryGetProperty("postalCode", out var postalElement))
                    {
                        warnings.Add("address missing 'postalCode' property");
                    }
                    else
                    {
                        addressParts.Add(postalElement.GetString() ?? "");
                    }
                    
                    if (!addressElement.TryGetProperty("addressCountry", out var countryElement))
                    {
                        warnings.Add("address missing 'addressCountry' property");
                    }
                    else
                    {
                        addressParts.Add(countryElement.GetString() ?? "");
                    }
                    
                    // Build full address for NAP tracking
                    address = string.Join(", ", addressParts.Where(p => !string.IsNullOrWhiteSpace(p)));
                }
                else if (addressElement.ValueKind == JsonValueKind.String)
                {
                    warnings.Add("address should be a structured PostalAddress object, not a string");
                    address = addressElement.GetString();
                }
            }
            
            // Telephone is important for local businesses
            if (!root.TryGetProperty("telephone", out var telephoneElement))
            {
                warnings.Add("Missing 'telephone' (important for local visibility and Google My Business)");
            }
            else
            {
                var telephone = telephoneElement.GetString();
                if (!string.IsNullOrEmpty(telephone))
                {
                    // Check if it looks like international format (starts with +)
                    if (!telephone.StartsWith("+"))
                    {
                        warnings.Add($"telephone '{telephone}' should use international format (e.g., +1-555-555-5555)");
                    }
                }
            }
            
            // Validate openingHours if present
            if (root.TryGetProperty("openingHours", out var hoursElement))
            {
                if (hoursElement.ValueKind == JsonValueKind.Array)
                {
                    var hoursArray = hoursElement.EnumerateArray().ToList();
                    if (hoursArray.Count == 0)
                    {
                        warnings.Add("openingHours array is empty");
                    }
                    else
                    {
                        // Check format (should be like "Mo-Fr 09:00-17:00")
                        foreach (var hour in hoursArray.Take(1)) // Just check first one
                        {
                            var hourString = hour.GetString();
                            if (string.IsNullOrEmpty(hourString) || 
                                (!hourString.Contains("-") && !hourString.Contains(":")))
                            {
                                warnings.Add($"openingHours format may be incorrect (use: 'Mo-Fr 09:00-17:00')");
                                break;
                            }
                        }
                    }
                }
                else if (hoursElement.ValueKind == JsonValueKind.String)
                {
                    warnings.Add("openingHours should be an array, not a string");
                }
            }
            
            // Geo coordinates recommended for local businesses
            if (!root.TryGetProperty("geo", out _))
            {
                warnings.Add("Missing 'geo' coordinates (recommended for local search accuracy)");
            }
            
            // Track NAP info for consistency checking
            if (!string.IsNullOrWhiteSpace(businessName) && !string.IsNullOrWhiteSpace(phone))
            {
                TrackNAP(ctx.Project.ProjectId, ctx.Url.ToString(), businessName, address ?? "", phone);
            }
            
            // Check NAP consistency across site
            await CheckNAPConsistencyAsync(ctx, businessName, address, phone);
        }

        if (missingProps.Any())
        {
            var missingList = string.Join(", ", missingProps);
            var localMissingDescription = $"LocalBusiness schema missing required property(ies) ({missingList}); add them so local pack visibility is not harmed.";
            var rowLocal = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", schemaType)
                .Set("Issue", "Incomplete LocalBusiness Schema")
                .Set("Description", localMissingDescription)
                .Set("Property", missingList)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowLocal, ctx.Metadata.UrlId, default);
        }
        
        // Report warnings for LocalBusiness
        if (warnings.Any() && schemaType == "LocalBusiness")
        {
            var warningPreview = string.Join("; ", warnings.Take(2));
            var localWarningDescription = $"LocalBusiness schema has {warnings.Count} recommendations (e.g., {warningPreview}); address them to keep local search data accurate.";
            var rowLocalWarn = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "LocalBusiness")
                .Set("Issue", $"LocalBusiness Recommendations ({warnings.Count})")
                .Set("Description", localWarningDescription)
                .Set("Property", string.Join(", ", warnings.Take(2)))
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowLocalWarn, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateBreadcrumbSchemaAsync(UrlContext ctx, JsonElement root)
    {
        if (!root.TryGetProperty("itemListElement", out var itemsElement))
        {
            var breadMissingDescription = "BreadcrumbList needs an 'itemListElement' array so search engines can read the navigation trail.";
            var rowBreadInv = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "BreadcrumbList")
                .Set("Issue", "Missing itemListElement")
                .Set("Description", breadMissingDescription)
                .Set("Property", "itemListElement")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowBreadInv, ctx.Metadata.UrlId, default);
            return;
        }
        
        // Validate breadcrumb structure
        if (itemsElement.ValueKind == JsonValueKind.Array)
        {
            var items = itemsElement.EnumerateArray().ToList();
            if (items.Count == 0)
            {
                var breadEmptyDescription = "BreadcrumbList exists but 'itemListElement' has no entries; add breadcrumb steps to render a trail.";
                var rowBreadEmpty = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "BreadcrumbList")
                    .Set("Issue", "Empty BreadcrumbList")
                    .Set("Description", breadEmptyDescription)
                    .Set("Property", "itemListElement")
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowBreadEmpty, ctx.Metadata.UrlId, default);
            }
            else
            {
                var breadcrumbLevels = items.Count > 0 ? $"{items.Count} levels" : "(none)";
                var breadcrumbDescription = $"BreadcrumbList with {items.Count} level(s) is present; this helps search engines show navigation paths.";
                var rowBreadFound = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "BreadcrumbList")
                    .Set("Issue", $"Breadcrumb Found ({items.Count} levels)")
                    .Set("Description", breadcrumbDescription)
                    .Set("Property", breadcrumbLevels)
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowBreadFound, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task ValidateHowToFaqSchemaAsync(UrlContext ctx, JsonElement root, string schemaType)
    {
        var requiredProp = schemaType == "FAQPage" ? "mainEntity" : "step";

        if (!root.TryGetProperty(requiredProp, out var mainEntityElement))
        {
            var missingRequiredDescription = schemaType switch
            {
                "FAQPage" => "FAQPage requires a 'mainEntity' array of question/answer pairs for the FAQ rich snippet.",
                "HowTo" => "HowTo schema requires a 'step' property describing each instruction; without it the schema is incomplete.",
                _ => $"Schema {schemaType} requires '{requiredProp}' to describe its content."
            };
            var rowFaqHow = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", schemaType)
                .Set("Issue", $"Missing {requiredProp}")
                .Set("Description", missingRequiredDescription)
                .Set("Property", requiredProp)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowFaqHow, ctx.Metadata.UrlId, default);
            return;
        }
        
        // Enhanced validation for FAQPage
        if (schemaType == "FAQPage" && mainEntityElement.ValueKind == JsonValueKind.Array)
        {
            var questions = mainEntityElement.EnumerateArray().ToList();
            var questionsWithoutAnswers = 0;
            var questionsWithShortAnswers = 0;
            
            foreach (var question in questions)
            {
                // Each question should have acceptedAnswer
                if (!question.TryGetProperty("acceptedAnswer", out var answerElement))
                {
                    questionsWithoutAnswers++;
                }
                else
                {
                    // Validate answer has substance (check text property)
                    if (answerElement.TryGetProperty("text", out var textElement))
                    {
                        var answerText = textElement.GetString();
                        if (!string.IsNullOrEmpty(answerText) && answerText.Length < 50)
                        {
                            questionsWithShortAnswers++;
                        }
                    }
                    else
                    {
                        questionsWithoutAnswers++;
                    }
                }
            }
            
            // Report issues with FAQ structure
            if (questionsWithoutAnswers > 0)
            {
                var faqMissingAnswerDescription = $"FAQPage has {questionsWithoutAnswers} question(s) without an acceptedAnswer; add meaningful answers for each to qualify for FAQ snippets.";
                var rowFaqMiss = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "FAQPage")
                    .Set("Issue", $"FAQ Missing Answers ({questionsWithoutAnswers})")
                    .Set("Description", faqMissingAnswerDescription)
                    .Set("Property", "acceptedAnswer")
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, rowFaqMiss, ctx.Metadata.UrlId, default);
            }
            else if (questionsWithShortAnswers == 0 && questions.Count > 0)
            {
                var faqSummary = $"{questions.Count} Q&A entries";
                var faqCompleteDescription = $"FAQPage has {questions.Count} complete Q&A entries; these are ready for FAQ rich snippets.";
                var rowFaqComplete = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "FAQPage")
                    .Set("Issue", $"FAQ Complete ({questions.Count} Q&As)")
                    .Set("Description", faqCompleteDescription)
                    .Set("Property", faqSummary)
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowFaqComplete, ctx.Metadata.UrlId, default);
            }
            
            if (questionsWithShortAnswers > 0)
            {
                var faqShortDescription = $"FAQPage has {questionsWithShortAnswers} answer(s) shorter than 50 characters; lengthen them to provide more helpful answers.";
                var rowFaqShort = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "FAQPage")
                    .Set("Issue", $"FAQ Short Answers ({questionsWithShortAnswers})")
                    .Set("Description", faqShortDescription)
                    .Set("Property", "acceptedAnswer")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowFaqShort, ctx.Metadata.UrlId, default);
            }
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
            var microProperty = string.Join(", ", distinctTypes);
            var microDescription = $"Detected {itemTypes.Count} microdata item(s) covering {microProperty}. Validate each so search engines can trust the markup.";
            
            var rowMicro = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", microProperty)
                    .Set("Issue", $"Microdata Found ({itemTypes.Count} items)")
                    .Set("Description", microDescription)
                    .Set("Property", microProperty)
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowMicro, ctx.Metadata.UrlId, default);
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
                var propertyValue = recommendedSchema ?? "(recommended schema)";
                var missingDescription = $"No structured data was detected; consider adding {propertyValue} markup so search engines understand this page.";
                var rowMissing = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", recommendedSchema)
                    .Set("Issue", "Missing Structured Data")
                    .Set("Description", missingDescription)
                    .Set("Property", propertyValue)
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowMissing, ctx.Metadata.UrlId, default);
            }
        }
    }
    
    /// <summary>
    /// Check for author markup on article pages
    /// </summary>
    private async Task CheckAuthorMarkupAsync(UrlContext ctx, HtmlDocument doc)
    {
        // Only check if this appears to be an article page
        var url = ctx.Url.ToString().ToLowerInvariant();
        var isArticlePage = Regex.IsMatch(url, @"/(blog|article|post|news)/") ||
                           doc.DocumentNode.SelectSingleNode("//article") != null;
        
        if (!isArticlePage)
        {
            return;
        }
        
        // Check for rel="author" links
        var authorLinks = doc.DocumentNode.SelectNodes("//a[@rel='author'] | //link[@rel='author']");
        var hasAuthorLink = authorLinks != null && authorLinks.Count > 0;
        
        // Check for author in visible content (bylines)
        var bodyText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? "";
        var bodyTextLower = bodyText.ToLowerInvariant();
        var hasAuthorByline = Regex.IsMatch(bodyText, @"\bby\s+[A-Z][a-z]+\s+[A-Z][a-z]+\b") ||
                             Regex.IsMatch(bodyTextLower, @"\bauthor:\s*\w+") ||
                             Regex.IsMatch(bodyTextLower, @"\bwritten by\s+\w+");
        
        if (!hasAuthorLink && !hasAuthorByline)
        {
            var authorDescription = "Article lacks rel='author' links or a clear author byline; add author markup so Google can tie this content to the creator.";
            var rowAuthor = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "Article")
                .Set("Issue", "Missing Author Markup")
                .Set("Description", authorDescription)
                .Set("Property", "author")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, rowAuthor, ctx.Metadata.UrlId, default);
        }
    }
    
    /// <summary>
    /// Scan page for contact information (phones and addresses)
    /// </summary>
    private async Task ScanContactInformationAsync(UrlContext ctx, HtmlDocument doc)
    {
        // Only check contact, about, or home pages
        var url = ctx.Url.ToString().ToLowerInvariant();
        var isContactPage = Regex.IsMatch(url, @"/(contact|about|company|location)/") ||
                           ctx.Url.AbsolutePath == "/" || ctx.Url.AbsolutePath == "";
        
        if (!isContactPage && ctx.Metadata.Depth > 1)
        {
            return; // Skip non-contact pages deeper in the site
        }
        
        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        if (bodyNode == null)
        {
            return;
        }
        
        var bodyText = bodyNode.InnerText ?? "";
        
        // Pattern match for phone numbers
        var phonePatterns = new[]
        {
            @"\+\d{1,3}[\s\-]?\(?\d{1,4}\)?[\s\-]?\d{1,4}[\s\-]?\d{1,9}", // International: +1 (555) 123-4567
            @"\(\d{3}\)\s*\d{3}[\-\s]?\d{4}", // US format: (555) 123-4567
            @"\d{3}[\-\.\s]\d{3}[\-\.\s]\d{4}" // Various formats: 555-123-4567, 555.123.4567
        };
        
        var hasPhone = phonePatterns.Any(pattern => Regex.IsMatch(bodyText, pattern));
        
        // Pattern match for addresses (simplified - city, state, zip combinations)
        var addressPatterns = new[]
        {
            @"\d+\s+[A-Z][a-z]+\s+(Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Court|Ct|Way)", // Street address
            @"[A-Z][a-z]+,\s*[A-Z]{2}\s+\d{5}", // City, ST 12345
            @"\d{5}(?:\-\d{4})?" // ZIP code
        };
        
        var hasAddress = addressPatterns.Any(pattern => Regex.IsMatch(bodyText, pattern));
        
        // Report findings for contact pages
        if (isContactPage)
        {
            if (!hasPhone && !hasAddress)
            {
                var contactDescription = "Contact page is missing both phone and address; add at least one so visitors and search engines can reach out.";
                var rowContact = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "Contact")
                    .Set("Issue", "Missing Contact Info")
                    .Set("Description", contactDescription)
                    .Set("Property", "phone, address")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowContact, ctx.Metadata.UrlId, default);
            }
            else if (!hasPhone)
            {
                var phoneDescription = "Contact page missing a phone number; include a properly formatted number for credibility.";
                var rowPhone = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "Contact")
                    .Set("Issue", "Missing Phone Number")
                    .Set("Description", phoneDescription)
                    .Set("Property", "phone")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowPhone, ctx.Metadata.UrlId, default);
            }
            else if (!hasAddress)
            {
                var addressDescription = "Contact page missing an address; add street/city/zip or structured PostalAddress markup.";
                var rowAddr = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("SchemaType", "Contact")
                    .Set("Issue", "Missing Address")
                    .Set("Description", addressDescription)
                    .Set("Property", "address")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, rowAddr, ctx.Metadata.UrlId, default);
            }
        }
    }
    
    /// <summary>
    /// Track NAP (Name, Address, Phone) information for consistency checking
    /// </summary>
    private void TrackNAP(int projectId, string url, string name, string address, string phone)
    {
        var napBag = NAPByProject.GetOrAdd(projectId, _ => new ConcurrentBag<NAPInfo>());
        napBag.Add(new NAPInfo
        {
            Url = url,
            Name = name,
            Address = address,
            Phone = phone
        });
    }
    
    /// <summary>
    /// Check NAP consistency across all LocalBusiness schemas on the site
    /// </summary>
    private async Task CheckNAPConsistencyAsync(UrlContext ctx, string? currentName, string? currentAddress, string? currentPhone)
    {
        if (!NAPByProject.TryGetValue(ctx.Project.ProjectId, out var napBag))
        {
            return; // First LocalBusiness found
        }
        
        var allNAPs = napBag.ToList();
        if (allNAPs.Count < 2)
        {
            return; // Need at least 2 to compare
        }
        
        // Check for inconsistencies
        var uniqueNames = allNAPs.Select(n => n.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniqueAddresses = allNAPs.Select(n => n.Address).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniquePhones = allNAPs.Select(n => n.Phone).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        
        var hasInconsistency = uniqueNames.Count > 1 || uniqueAddresses.Count > 1 || uniquePhones.Count > 1;
        
        // Only report once per project using TryAdd (atomic operation)
        if (hasInconsistency && NAPInconsistencyReportedByProject.TryAdd(ctx.Project.ProjectId, true))
        {
            var napDescription = $"LocalBusiness markup varies across the site ({uniqueNames.Count} names, {uniqueAddresses.Count} addresses, {uniquePhones.Count} phones); keep NAP consistent for stronger local signals.";
            var rowNAP = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("SchemaType", "LocalBusiness")
                .Set("Issue", $"NAP Inconsistency ({uniqueNames.Count} names, {uniquePhones.Count} phones)")
                .Set("Description", napDescription)
                .Set("Property", "name, address, telephone")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, rowNAP, ctx.Metadata.UrlId, default);
        }
    }
    
    public override void CleanupProject(int projectId)
    {
        NAPByProject.TryRemove(projectId, out _);
        NAPInconsistencyReportedByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up structured data tracking for project {ProjectId}", projectId);
    }
    
    /// <summary>
    /// Safely tries to get a double value from a JsonElement, handling both string and number formats.
    /// Schema.org allows numeric properties to be either strings or numbers.
    /// </summary>
    private static bool TryGetDoubleValue(JsonElement element, out double value)
    {
        // Try as a number first
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }
        
        // Try as a string
        if (element.ValueKind == JsonValueKind.String)
        {
            var stringValue = element.GetString();
            if (!string.IsNullOrEmpty(stringValue) && double.TryParse(stringValue, out value))
            {
                return true;
            }
        }
        
        value = 0;
        return false;
    }
    
    /// <summary>
    /// Safely tries to get an int32 value from a JsonElement, handling both string and number formats.
    /// Schema.org allows numeric properties to be either strings or numbers.
    /// </summary>
    private static bool TryGetInt32Value(JsonElement element, out int value)
    {
        // Try as a number first
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }
        
        // Try as a string
        if (element.ValueKind == JsonValueKind.String)
        {
            var stringValue = element.GetString();
            if (!string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, out value))
            {
                return true;
            }
        }
        
        value = 0;
        return false;
    }
    
    /// <summary>
    /// NAP (Name, Address, Phone) information for consistency checking
    /// </summary>
    private class NAPInfo
    {
        public string Url { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}


