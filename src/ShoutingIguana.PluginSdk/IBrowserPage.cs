namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Abstraction over browser page for plugin access.
/// Provides common operations without exposing Playwright directly.
/// </summary>
public interface IBrowserPage
{
    /// <summary>
    /// Get the full rendered HTML content.
    /// </summary>
    Task<string> ContentAsync();
    
    /// <summary>
    /// Query for a single element matching the CSS selector.
    /// </summary>
    Task<IElementHandle?> QuerySelectorAsync(string selector);
    
    /// <summary>
    /// Query for all elements matching the CSS selector.
    /// </summary>
    Task<IElementHandle[]> QuerySelectorAllAsync(string selector);
    
    /// <summary>
    /// Evaluate JavaScript expression and return result.
    /// </summary>
    Task<T> EvaluateAsync<T>(string expression);
    
    /// <summary>
    /// Evaluate JavaScript function with an element as parameter.
    /// </summary>
    Task<T> EvaluateAsync<T>(string pageFunction, IElementHandle element);
    
    /// <summary>
    /// Get attribute value from an element.
    /// </summary>
    Task<string?> GetAttributeAsync(IElementHandle element, string name);
    
    /// <summary>
    /// Get text content from an element.
    /// </summary>
    Task<string?> GetTextContentAsync(IElementHandle element);
}

/// <summary>
/// Represents a handle to a DOM element.
/// </summary>
public interface IElementHandle
{
    /// <summary>
    /// Get attribute value.
    /// </summary>
    Task<string?> GetAttributeAsync(string name);
    
    /// <summary>
    /// Get text content.
    /// </summary>
    Task<string?> TextContentAsync();
    
    /// <summary>
    /// Get inner HTML.
    /// </summary>
    Task<string?> InnerHtmlAsync();
}

