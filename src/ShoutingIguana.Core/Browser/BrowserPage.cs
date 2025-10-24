using Microsoft.Playwright;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Browser;

/// <summary>
/// Wrapper around Playwright IPage that implements the plugin SDK's IBrowserPage interface.
/// </summary>
public class BrowserPage(IPage page) : ShoutingIguana.PluginSdk.IBrowserPage
{
    private readonly IPage _page = page;

    public async Task<string> ContentAsync()
    {
        return await _page.ContentAsync();
    }

    public async Task<ShoutingIguana.PluginSdk.IElementHandle?> QuerySelectorAsync(string selector)
    {
        var element = await _page.QuerySelectorAsync(selector);
        return element != null ? new ElementHandleWrapper(element) : null;
    }

    public async Task<ShoutingIguana.PluginSdk.IElementHandle[]> QuerySelectorAllAsync(string selector)
    {
        var elements = await _page.QuerySelectorAllAsync(selector);
        return elements.Select(e => new ElementHandleWrapper(e)).ToArray<ShoutingIguana.PluginSdk.IElementHandle>();
    }

    public async Task<T> EvaluateAsync<T>(string expression)
    {
        return await _page.EvaluateAsync<T>(expression);
    }

    public async Task<string?> GetAttributeAsync(ShoutingIguana.PluginSdk.IElementHandle element, string name)
    {
        if (element is ElementHandleWrapper wrapper)
        {
            return await wrapper.GetAttributeAsync(name);
        }
        return null;
    }

    public async Task<string?> GetTextContentAsync(ShoutingIguana.PluginSdk.IElementHandle element)
    {
        if (element is ElementHandleWrapper wrapper)
        {
            return await wrapper.TextContentAsync();
        }
        return null;
    }
}

/// <summary>
/// Wrapper around Playwright IElementHandle.
/// </summary>
public class ElementHandleWrapper(Microsoft.Playwright.IElementHandle handle) : ShoutingIguana.PluginSdk.IElementHandle
{
    private readonly Microsoft.Playwright.IElementHandle _handle = handle;

    public async Task<string?> GetAttributeAsync(string name)
    {
        return await _handle.GetAttributeAsync(name);
    }

    public async Task<string?> TextContentAsync()
    {
        return await _handle.TextContentAsync();
    }

    public async Task<string?> InnerHtmlAsync()
    {
        return await _handle.InnerHTMLAsync();
    }
}

