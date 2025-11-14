using HtmlAgilityPack;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Services;

public class LinkExtractor : ILinkExtractor
{
    public Task<IEnumerable<ExtractedLink>> ExtractLinksAsync(string htmlContent, string baseUrl)
    {
        var links = new List<ExtractedLink>();
        
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var baseUri = new Uri(baseUrl);
            
            // Extract base tag if present (respects browser behavior for relative URLs)
            Uri? baseTagUri = null;
            var baseNode = doc.DocumentNode.SelectSingleNode("//base[@href]");
            if (baseNode != null)
            {
                var baseHref = baseNode.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrWhiteSpace(baseHref))
                {
                    // Try to parse as absolute URI
                    if (Uri.TryCreate(baseHref, UriKind.Absolute, out var absoluteBaseUri))
                    {
                        baseTagUri = absoluteBaseUri;
                    }
                    // Try to resolve as relative to current page
                    else if (Uri.TryCreate(baseUri, baseHref, out var resolvedBaseUri))
                    {
                        baseTagUri = resolvedBaseUri;
                    }
                }
            }

            // Extract hyperlinks
            var anchorNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchorNodes != null)
            {
                foreach (var node in anchorNodes)
                {
                    var href = node.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    var resolvedUrl = ResolveUrl(href, baseUri, baseTagUri);
                    if (resolvedUrl != null)
                    {
                        var rel = node.GetAttributeValue("rel", string.Empty);
                        links.Add(new ExtractedLink
                        {
                            Url = resolvedUrl,
                            AnchorText = node.InnerText.Trim(),
                            LinkType = LinkType.Hyperlink,
                            RelAttribute = string.IsNullOrEmpty(rel) ? null : rel
                        });
                    }
                }
            }

            // Extract images
            var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
            if (imgNodes != null)
            {
                foreach (var node in imgNodes)
                {
                    var src = node.GetAttributeValue("src", string.Empty);
                    if (string.IsNullOrWhiteSpace(src))
                        continue;

                    var resolvedUrl = ResolveUrl(src, baseUri, baseTagUri);
                    if (resolvedUrl != null)
                    {
                        links.Add(new ExtractedLink
                        {
                            Url = resolvedUrl,
                            AnchorText = node.GetAttributeValue("alt", null),
                            LinkType = LinkType.Image
                        });
                    }
                }
            }

            // Extract stylesheets
            var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet' and @href]");
            if (linkNodes != null)
            {
                foreach (var node in linkNodes)
                {
                    var href = node.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    var resolvedUrl = ResolveUrl(href, baseUri, baseTagUri);
                    if (resolvedUrl != null)
                    {
                        links.Add(new ExtractedLink
                        {
                            Url = resolvedUrl,
                            LinkType = LinkType.Stylesheet
                        });
                    }
                }
            }

            // Extract scripts
            var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
            if (scriptNodes != null)
            {
                foreach (var node in scriptNodes)
                {
                    var src = node.GetAttributeValue("src", string.Empty);
                    if (string.IsNullOrWhiteSpace(src))
                        continue;

                    var resolvedUrl = ResolveUrl(src, baseUri, baseTagUri);
                    if (resolvedUrl != null)
                    {
                        links.Add(new ExtractedLink
                        {
                            Url = resolvedUrl,
                            LinkType = LinkType.Script
                        });
                    }
                }
            }
        }
        catch (Exception)
        {
            // Log error but don't fail - return empty list
        }

        return Task.FromResult<IEnumerable<ExtractedLink>>(links);
    }

    private static string? ResolveUrl(string url, Uri baseUri, Uri? baseTagUri)
    {
        try
        {
            // Skip javascript:, mailto:, tel:, etc.
            if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("#"))
            {
                return null;
            }

            var schemeSource = baseTagUri ?? baseUri;
            var normalizedUrl = url;
            if (normalizedUrl.StartsWith("//", StringComparison.Ordinal))
            {
                // Scheme-relative URL (e.g., //cdn.example.com/file.js) should inherit the current scheme
                normalizedUrl = $"{schemeSource.Scheme}:{normalizedUrl}";
            }

            Uri absoluteUri;
            if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var parsedUri))
            {
                absoluteUri = parsedUri;
            }
            else
            {
                // Use base tag URI if present, otherwise use page URI
                if (Uri.TryCreate(schemeSource, normalizedUrl, out parsedUri))
                {
                    absoluteUri = parsedUri;
                }
                else
                {
                    return null;
                }
            }

            // Remove fragment
            var builder = new UriBuilder(absoluteUri) { Fragment = string.Empty };
            return builder.Uri.ToString();
        }
        catch
        {
            return null;
        }
    }
}

