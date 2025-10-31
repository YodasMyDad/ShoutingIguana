namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Fluent builder for creating structured FindingDetails with hierarchical information.
/// This builder makes it easy to construct well-organized finding reports with nested sections,
/// user-friendly details, and technical metadata.
/// </summary>
/// <remarks>
/// <para>
/// The builder supports two types of information:
/// - <b>User-facing details</b>: Organized hierarchically with sections and bullet points
/// - <b>Technical metadata</b>: Hidden by default, for debugging and advanced analysis
/// </para>
/// <para>
/// Use nested sections to organize complex findings into logical groups like 
/// "Issue", "Impact", "Recommendations", etc.
/// </para>
/// </remarks>
/// <example>
/// <para><b>Simple finding with flat items:</b></para>
/// <code>
/// var details = FindingDetailsBuilder.Create()
///     .AddItem("Page URL: https://example.com/page")
///     .AddItem("Title length: 72 characters")
///     .AddItem("⚠️ Title exceeds recommended 60 characters")
///     .Build();
/// </code>
/// 
/// <para><b>Complex finding with nested sections:</b></para>
/// <code>
/// var details = FindingDetailsBuilder.Create()
///     .AddItem($"Page: {ctx.Url}")
///     .AddItem($"Status: {statusCode}")
///     .BeginNested("📉 SEO Impact")
///         .AddItem("Page will not rank in search results")
///         .AddItem("Link equity is not passed")
///     .EndNested()
///     .BeginNested("💡 Recommendations")
///         .AddItem("Remove noindex directive from meta robots tag")
///         .AddItem("Verify robots.txt doesn't block this page")
///     .EndNested()
///     .WithTechnicalMetadata("httpStatus", 200)
///     .WithTechnicalMetadata("robotsTag", "noindex, nofollow")
///     .Build();
/// 
/// await ctx.Findings.ReportAsync(
///     Key, 
///     Severity.Warning, 
///     "NOINDEX_DETECTED", 
///     "Page has noindex directive", 
///     details);
/// </code>
/// 
/// <para><b>Quick helpers for simple cases:</b></para>
/// <code>
/// // Simple flat list
/// var details = FindingDetailsBuilder.Simple(
///     "Page URL: https://example.com",
///     "Missing H1 tag",
///     "H1 is important for SEO"
/// );
/// 
/// // With technical metadata
/// var details = FindingDetailsBuilder.WithMetadata(
///     new Dictionary&lt;string, object?&gt; { ["url"] = url, ["status"] = 200 },
///     "Page loaded successfully",
///     "Title found: Example Page"
/// );
/// </code>
/// </example>
public class FindingDetailsBuilder
{
    private readonly FindingDetails _details;
    private readonly Stack<FindingDetail> _nestedStack = new();
    private FindingDetail? _currentParent;

    private FindingDetailsBuilder()
    {
        _details = new FindingDetails();
    }

    /// <summary>
    /// Creates a new FindingDetailsBuilder instance.
    /// </summary>
    /// <returns>A new builder instance ready to add items.</returns>
    /// <example>
    /// <code>
    /// var builder = FindingDetailsBuilder.Create();
    /// builder.AddItem("First item")
    ///        .AddItem("Second item")
    ///        .Build();
    /// </code>
    /// </example>
    public static FindingDetailsBuilder Create()
    {
        return new FindingDetailsBuilder();
    }

    /// <summary>
    /// Adds a detail item to the current level.
    /// If called within a nested section, adds to that section; otherwise adds to the top level.
    /// </summary>
    /// <param name="text">The text content of the detail item. Can include emojis for visual emphasis.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// builder.AddItem("Page URL: https://example.com")
    ///        .AddItem("⚠️ Missing meta description")
    ///        .AddItem("✅ Title tag is present");
    /// </code>
    /// </example>
    public FindingDetailsBuilder AddItem(string text)
    {
        var item = new FindingDetail { Text = text };
        
        if (_currentParent != null)
        {
            // We're inside a nested section
            _currentParent.Children ??= [];
            _currentParent.Children.Add(item);
        }
        else
        {
            // Top-level item
            _details.Items.Add(item);
        }
        
        return this;
    }

    /// <summary>
    /// Adds a detail item with additional metadata attached to this specific item.
    /// </summary>
    /// <param name="text">The text content of the detail item.</param>
    /// <param name="metadata">Item-specific metadata (different from technical metadata at the finding level).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// This is rarely needed. Most metadata should go in <see cref="WithTechnicalMetadata(string, object?)"/> instead.
    /// Use this only when specific metadata needs to be attached to individual items.
    /// </remarks>
    public FindingDetailsBuilder AddItem(string text, Dictionary<string, object?> metadata)
    {
        var item = new FindingDetail 
        { 
            Text = text,
            Metadata = metadata
        };
        
        if (_currentParent != null)
        {
            _currentParent.Children ??= [];
            _currentParent.Children.Add(item);
        }
        else
        {
            _details.Items.Add(item);
        }
        
        return this;
    }

    /// <summary>
    /// Begins a nested section with a header.
    /// All subsequent <see cref="AddItem(string)"/> calls will be nested under this section 
    /// until <see cref="EndNested"/> is called.
    /// </summary>
    /// <param name="headerText">The header text for the nested section. Use emojis to make sections visually distinct.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// Nested sections can contain other nested sections (multi-level nesting is supported).
    /// Common header prefixes: "📉 SEO Impact", "💡 Recommendations", "⚠️ Issues", "✅ Passed", "📊 Statistics"
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddItem("Page has noindex directive")
    ///        .BeginNested("💡 Recommendations")
    ///            .AddItem("Remove noindex from meta robots")
    ///            .AddItem("Check robots.txt configuration")
    ///        .EndNested();
    /// </code>
    /// </example>
    public FindingDetailsBuilder BeginNested(string headerText)
    {
        var nestedParent = new FindingDetail 
        { 
            Text = headerText,
            Children = []
        };
        
        if (_currentParent != null)
        {
            // Nested within another nested section
            _currentParent.Children ??= [];
            _currentParent.Children.Add(nestedParent);
            _nestedStack.Push(_currentParent);
        }
        else
        {
            // Top-level nested section
            _details.Items.Add(nestedParent);
        }
        
        _currentParent = nestedParent;
        return this;
    }

    /// <summary>
    /// Ends the current nested section, returning to the parent level.
    /// After calling this, subsequent <see cref="AddItem(string)"/> calls will be added to the parent level.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// This method is optional - <see cref="Build"/> automatically closes any open nested sections.
    /// Use <see cref="EndNested"/> when you want explicit control over nesting levels, especially
    /// for multi-level nested structures or when adding items at different levels.
    /// </remarks>
    public FindingDetailsBuilder EndNested()
    {
        if (_nestedStack.Count > 0)
        {
            _currentParent = _nestedStack.Pop();
        }
        else
        {
            _currentParent = null;
        }
        
        return this;
    }

    /// <summary>
    /// Adds technical metadata that will be hidden by default but available for advanced users and debugging.
    /// This is ideal for diagnostic data, raw API responses, detailed timing information, etc.
    /// </summary>
    /// <param name="key">The metadata key (use camelCase convention).</param>
    /// <param name="value">The metadata value (will be JSON-serialized in exports).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// Technical metadata is:
    /// - Hidden from the main UI by default
    /// - Included in CSV/Excel exports when "Include Technical Data" is checked
    /// - Serialized as JSON for export
    /// - Useful for debugging and advanced analysis
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddItem("Link is broken")
    ///        .WithTechnicalMetadata("targetUrl", "https://example.com/page")
    ///        .WithTechnicalMetadata("httpStatus", 404)
    ///        .WithTechnicalMetadata("responseTime", 1.23)
    ///        .WithTechnicalMetadata("retryCount", 3);
    /// </code>
    /// </example>
    public FindingDetailsBuilder WithTechnicalMetadata(string key, object? value)
    {
        _details.TechnicalMetadata ??= [];
        _details.TechnicalMetadata[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple technical metadata entries at once.
    /// </summary>
    /// <param name="metadata">Dictionary of metadata key-value pairs to add.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// var metadata = new Dictionary&lt;string, object?&gt;
    /// {
    ///     ["url"] = ctx.Url.ToString(),
    ///     ["status"] = statusCode,
    ///     ["timestamp"] = DateTime.UtcNow
    /// };
    /// builder.WithTechnicalMetadata(metadata);
    /// </code>
    /// </example>
    public FindingDetailsBuilder WithTechnicalMetadata(Dictionary<string, object?> metadata)
    {
        _details.TechnicalMetadata ??= [];
        
        foreach (var kvp in metadata)
        {
            _details.TechnicalMetadata[kvp.Key] = kvp.Value;
        }
        
        return this;
    }

    /// <summary>
    /// Builds and returns the complete FindingDetails object.
    /// Call this after adding all items and sections.
    /// </summary>
    /// <returns>The constructed FindingDetails object ready to pass to <c>ctx.Findings.ReportAsync()</c>.</returns>
    /// <remarks>
    /// Any open nested sections are automatically closed when <see cref="Build"/> is called,
    /// so calling <see cref="EndNested"/> before <see cref="Build"/> is optional.
    /// </remarks>
    /// <example>
    /// <code>
    /// var details = builder
    ///     .AddItem("Finding description")
    ///     .Build();
    /// 
    /// await ctx.Findings.ReportAsync(
    ///     Key, 
    ///     Severity.Warning, 
    ///     "CODE", 
    ///     "Message", 
    ///     details);
    /// </code>
    /// </example>
    public FindingDetails Build()
    {
        // Auto-close any open nested sections for convenience
        while (_currentParent != null || _nestedStack.Count > 0)
        {
            if (_nestedStack.Count > 0)
            {
                _currentParent = _nestedStack.Pop();
            }
            else
            {
                _currentParent = null;
            }
        }
        
        return _details;
    }

    /// <summary>
    /// Helper method to quickly create a simple FindingDetails with just top-level items.
    /// Useful for straightforward findings that don't need nested sections.
    /// </summary>
    /// <param name="items">The detail items to include.</param>
    /// <returns>A complete FindingDetails object with the specified items.</returns>
    /// <example>
    /// <code>
    /// var details = FindingDetailsBuilder.Simple(
    ///     "Page URL: https://example.com",
    ///     "Missing H1 tag",
    ///     "H1 tags are important for SEO"
    /// );
    /// </code>
    /// </example>
    public static FindingDetails Simple(params string[] items)
    {
        var builder = Create();
        foreach (var item in items)
        {
            builder.AddItem(item);
        }
        return builder.Build();
    }

    /// <summary>
    /// Helper method to create FindingDetails with items and technical metadata in one call.
    /// Useful for simple findings that need to include diagnostic data.
    /// </summary>
    /// <param name="technicalMetadata">Technical metadata to include with the finding.</param>
    /// <param name="items">The detail items to include.</param>
    /// <returns>A complete FindingDetails object with items and technical metadata.</returns>
    /// <example>
    /// <code>
    /// var details = FindingDetailsBuilder.WithMetadata(
    ///     new Dictionary&lt;string, object?&gt; 
    ///     { 
    ///         ["url"] = ctx.Url.ToString(),
    ///         ["status"] = 200 
    ///     },
    ///     "Page loaded successfully",
    ///     "Title: Example Page",
    ///     "Status: OK"
    /// );
    /// </code>
    /// </example>
    public static FindingDetails WithMetadata(Dictionary<string, object?> technicalMetadata, params string[] items)
    {
        var builder = Create();
        foreach (var item in items)
        {
            builder.AddItem(item);
        }
        builder.WithTechnicalMetadata(technicalMetadata);
        return builder.Build();
    }
}

