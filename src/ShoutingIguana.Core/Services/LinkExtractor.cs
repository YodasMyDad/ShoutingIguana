using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Services;

public class LinkExtractor(ILogger<LinkExtractor> logger) : ILinkExtractor
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

            // Extract iframes (same-origin frames contribute to the crawl graph).
            var iframeNodes = doc.DocumentNode.SelectNodes("//iframe[@src]");
            if (iframeNodes != null)
            {
                foreach (var node in iframeNodes)
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
                            LinkType = LinkType.Other
                        });
                    }
                }
            }

            // Extract srcset candidates from <img srcset>, <source srcset> (picture),
            // and <source srcset> (video/audio). Each comma-separated entry is a
            // candidate URL; the descriptor (e.g. " 2x" or " 640w") is ignored.
            var srcsetNodes = doc.DocumentNode.SelectNodes("//*[@srcset]");
            if (srcsetNodes != null)
            {
                foreach (var node in srcsetNodes)
                {
                    var srcset = node.GetAttributeValue("srcset", string.Empty);
                    if (string.IsNullOrWhiteSpace(srcset))
                        continue;

                    foreach (var candidate in ParseSrcset(srcset))
                    {
                        var resolvedUrl = ResolveUrl(candidate, baseUri, baseTagUri);
                        if (resolvedUrl != null)
                        {
                            links.Add(new ExtractedLink
                            {
                                Url = resolvedUrl,
                                LinkType = LinkType.Image,
                                AnchorText = node.GetAttributeValue("alt", null)
                            });
                        }
                    }
                }
            }

            // Extract media sources: <video src>, <audio src>, and <source src>
            // nested under video/audio/picture.
            var mediaSourceNodes = doc.DocumentNode.SelectNodes(
                "//video[@src] | //audio[@src] | //video/source[@src] | //audio/source[@src] | //picture/source[@src]");
            if (mediaSourceNodes != null)
            {
                foreach (var node in mediaSourceNodes)
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
                            LinkType = LinkType.Other
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract links from HTML for {BaseUrl}", baseUrl);
        }

        return Task.FromResult<IEnumerable<ExtractedLink>>(links);
    }

    private static IEnumerable<string> ParseSrcset(string srcset)
    {
        // srcset = image-candidate ("," image-candidate)*
        // image-candidate = url [whitespace descriptor]?
        foreach (var entry in srcset.Split(','))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // First whitespace-separated token is the URL; the rest is the width
            // or pixel-density descriptor which we do not care about for link discovery.
            var space = trimmed.IndexOfAny(new[] { ' ', '\t' });
            var candidate = space < 0 ? trimmed : trimmed[..space];
            if (candidate.Length > 0)
            {
                yield return candidate;
            }
        }
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
        catch (UriFormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

