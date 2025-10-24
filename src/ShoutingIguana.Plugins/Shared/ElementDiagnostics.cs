using ShoutingIguana.PluginSdk;
using System.Text.Json;

namespace ShoutingIguana.Plugins.Shared;

/// <summary>
/// Diagnostic information about a DOM element.
/// </summary>
public class ElementDiagnosticInfo
{
    public string TagName { get; set; } = string.Empty;
    public string DomPath { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
    public ParentElementInfo? ParentElement { get; set; }
    public BoundingBoxInfo? BoundingBox { get; set; }
    public bool IsVisible { get; set; }
    public string HtmlContext { get; set; } = string.Empty;
    public ComputedStyleInfo? ComputedStyle { get; set; }
}

public class ParentElementInfo
{
    public string TagName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public string? Id { get; set; }
}

public class BoundingBoxInfo
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ComputedStyleInfo
{
    public string? Display { get; set; }
    public string? Visibility { get; set; }
    public string? Opacity { get; set; }
}

/// <summary>
/// Helper class for extracting detailed diagnostic information about DOM elements.
/// </summary>
public static class ElementDiagnostics
{
    /// <summary>
    /// Gets comprehensive diagnostic information about an element.
    /// </summary>
    public static async Task<ElementDiagnosticInfo> GetElementInfoAsync(
        IBrowserPage page, 
        IElementHandle element)
    {
        var info = new ElementDiagnosticInfo();

        try
        {
            // Get tag name
            var tagNameResult = await page.EvaluateAsync<string>(
                @"(el) => el.tagName.toLowerCase()",
                element);
            info.TagName = tagNameResult ?? "unknown";

            // Get all attributes
            var attributesJson = await page.EvaluateAsync<string>(
                @"(el) => {
                    const attrs = {};
                    for (let i = 0; i < el.attributes.length; i++) {
                        const attr = el.attributes[i];
                        attrs[attr.name] = attr.value;
                    }
                    return JSON.stringify(attrs);
                }",
                element);
            
            if (!string.IsNullOrEmpty(attributesJson))
            {
                info.Attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(attributesJson) 
                    ?? new Dictionary<string, string>();
            }

            // Get DOM path (CSS selector path)
            info.DomPath = await GetCssPathAsync(page, element);

            // Get parent element info
            var parentJson = await page.EvaluateAsync<string>(
                @"(el) => {
                    if (!el.parentElement) return null;
                    return JSON.stringify({
                        tagName: el.parentElement.tagName.toLowerCase(),
                        className: el.parentElement.className || null,
                        id: el.parentElement.id || null
                    });
                }",
                element);

            if (!string.IsNullOrEmpty(parentJson))
            {
                info.ParentElement = JsonSerializer.Deserialize<ParentElementInfo>(parentJson);
            }

            // Get bounding box
            var boundingBoxJson = await page.EvaluateAsync<string>(
                @"(el) => {
                    const rect = el.getBoundingClientRect();
                    return JSON.stringify({
                        x: rect.x,
                        y: rect.y,
                        width: rect.width,
                        height: rect.height
                    });
                }",
                element);

            if (!string.IsNullOrEmpty(boundingBoxJson))
            {
                info.BoundingBox = JsonSerializer.Deserialize<BoundingBoxInfo>(boundingBoxJson);
            }

            // Check visibility
            info.IsVisible = await page.EvaluateAsync<bool>(
                @"(el) => {
                    const style = window.getComputedStyle(el);
                    return style.display !== 'none' && 
                           style.visibility !== 'hidden' && 
                           parseFloat(style.opacity) > 0;
                }",
                element);

            // Get computed styles
            var styleJson = await page.EvaluateAsync<string>(
                @"(el) => {
                    const style = window.getComputedStyle(el);
                    return JSON.stringify({
                        display: style.display,
                        visibility: style.visibility,
                        opacity: style.opacity
                    });
                }",
                element);

            if (!string.IsNullOrEmpty(styleJson))
            {
                info.ComputedStyle = JsonSerializer.Deserialize<ComputedStyleInfo>(styleJson);
            }

            // Get HTML context (outerHTML with limited length)
            var outerHtml = await element.InnerHtmlAsync() ?? "";
            if (outerHtml.Length > 500)
            {
                outerHtml = outerHtml.Substring(0, 500) + "...";
            }
            info.HtmlContext = $"<{info.TagName}>{outerHtml}</{info.TagName}>";
        }
        catch (Exception)
        {
            // Return partial info if any step fails
        }

        return info;
    }

    /// <summary>
    /// Gets the CSS selector path to an element.
    /// </summary>
    public static async Task<string> GetCssPathAsync(IBrowserPage page, IElementHandle element)
    {
        try
        {
            var path = await page.EvaluateAsync<string>(
                @"(el) => {
                    const path = [];
                    let current = el;
                    while (current && current.nodeType === Node.ELEMENT_NODE) {
                        let selector = current.tagName.toLowerCase();
                        
                        if (current.id) {
                            selector += '#' + current.id;
                            path.unshift(selector);
                            break; // ID is unique, stop here
                        } else if (current.className && typeof current.className === 'string') {
                            const classes = current.className.trim().split(/\s+/);
                            if (classes.length > 0 && classes[0]) {
                                selector += '.' + classes.join('.');
                            }
                        }
                        
                        // Add nth-child if not unique
                        if (current.parentElement) {
                            const siblings = Array.from(current.parentElement.children);
                            const sameTagSiblings = siblings.filter(s => s.tagName === current.tagName);
                            if (sameTagSiblings.length > 1) {
                                const index = sameTagSiblings.indexOf(current) + 1;
                                selector += ':nth-child(' + index + ')';
                            }
                        }
                        
                        path.unshift(selector);
                        current = current.parentElement;
                        
                        if (path.length > 10) break; // Limit path length
                    }
                    return path.join(' > ');
                }",
                element);
            
            return path ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}

