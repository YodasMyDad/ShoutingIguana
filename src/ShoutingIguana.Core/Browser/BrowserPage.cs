using Microsoft.Playwright;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Browser;

/// <summary>
/// Wrapper around Playwright IPage that implements the plugin SDK's IBrowserPage interface.
/// </summary>
public class BrowserPage(IPage page) : ShoutingIguana.PluginSdk.IBrowserPage
{
    public async Task<string> ContentAsync()
    {
        return await page.ContentAsync().ConfigureAwait(false);
    }

    public async Task<ShoutingIguana.PluginSdk.IElementHandle?> QuerySelectorAsync(string selector)
    {
        var element = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
        return element != null ? new ElementHandleWrapper(element) : null;
    }

    public async Task<ShoutingIguana.PluginSdk.IElementHandle[]> QuerySelectorAllAsync(string selector)
    {
        var elements = await page.QuerySelectorAllAsync(selector).ConfigureAwait(false);
        return elements.Select(e => new ElementHandleWrapper(e)).ToArray<ShoutingIguana.PluginSdk.IElementHandle>();
    }

    public async Task<T> EvaluateAsync<T>(string expression)
    {
        return await page.EvaluateAsync<T>(expression).ConfigureAwait(false);
    }

    public async Task<T> EvaluateAsync<T>(string pageFunction, ShoutingIguana.PluginSdk.IElementHandle element)
    {
        if (element is ElementHandleWrapper wrapper)
        {
            return await page.EvaluateAsync<T>(pageFunction, wrapper.Handle).ConfigureAwait(false);
        }
        throw new ArgumentException("Element must be an ElementHandleWrapper", nameof(element));
    }

    public async Task<string?> GetAttributeAsync(ShoutingIguana.PluginSdk.IElementHandle element, string name)
    {
        if (element is ElementHandleWrapper wrapper)
        {
            return await wrapper.GetAttributeAsync(name).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<string?> GetTextContentAsync(ShoutingIguana.PluginSdk.IElementHandle element)
    {
        if (element is ElementHandleWrapper wrapper)
        {
            return await wrapper.TextContentAsync().ConfigureAwait(false);
        }
        return null;
    }
}

/// <summary>
/// Wrapper around Playwright IElementHandle.
/// </summary>
public class ElementHandleWrapper(Microsoft.Playwright.IElementHandle handle) : ShoutingIguana.PluginSdk.IElementHandle
{
    internal Microsoft.Playwright.IElementHandle Handle => handle;

    public async Task<string?> GetAttributeAsync(string name)
    {
        return await handle.GetAttributeAsync(name).ConfigureAwait(false);
    }

    public async Task<string?> TextContentAsync()
    {
        return await handle.TextContentAsync().ConfigureAwait(false);
    }

    public async Task<string?> InnerHtmlAsync()
    {
        return await handle.InnerHTMLAsync().ConfigureAwait(false);
    }
}

