using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Shared;

/// <summary>
/// Fluent builder for creating structured FindingDetails.
/// Makes it easy for plugins to create hierarchical finding information.
/// </summary>
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
    public static FindingDetailsBuilder Create()
    {
        return new FindingDetailsBuilder();
    }

    /// <summary>
    /// Adds a top-level detail item.
    /// </summary>
    public FindingDetailsBuilder AddItem(string text)
    {
        var item = new FindingDetail { Text = text };
        
        if (_currentParent != null)
        {
            // We're inside a nested section
            _currentParent.Children ??= new List<FindingDetail>();
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
    /// Adds a detail item with metadata.
    /// </summary>
    public FindingDetailsBuilder AddItem(string text, Dictionary<string, object?> metadata)
    {
        var item = new FindingDetail 
        { 
            Text = text,
            Metadata = metadata
        };
        
        if (_currentParent != null)
        {
            _currentParent.Children ??= new List<FindingDetail>();
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
    /// All subsequent AddItem calls will be nested under this section until EndNested is called.
    /// </summary>
    public FindingDetailsBuilder BeginNested(string headerText)
    {
        var nestedParent = new FindingDetail 
        { 
            Text = headerText,
            Children = new List<FindingDetail>()
        };
        
        if (_currentParent != null)
        {
            // Nested within another nested section
            _currentParent.Children ??= new List<FindingDetail>();
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
    /// Ends the current nested section.
    /// </summary>
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
    /// Adds technical metadata that will be hidden by default but available for advanced users.
    /// </summary>
    public FindingDetailsBuilder WithTechnicalMetadata(string key, object? value)
    {
        _details.TechnicalMetadata ??= new Dictionary<string, object?>();
        _details.TechnicalMetadata[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple technical metadata entries at once.
    /// </summary>
    public FindingDetailsBuilder WithTechnicalMetadata(Dictionary<string, object?> metadata)
    {
        _details.TechnicalMetadata ??= new Dictionary<string, object?>();
        
        foreach (var kvp in metadata)
        {
            _details.TechnicalMetadata[kvp.Key] = kvp.Value;
        }
        
        return this;
    }

    /// <summary>
    /// Builds and returns the FindingDetails object.
    /// </summary>
    public FindingDetails Build()
    {
        // Ensure we're not still in a nested section
        if (_currentParent != null || _nestedStack.Count > 0)
        {
            throw new InvalidOperationException("Cannot build FindingDetails while still in a nested section. Call EndNested() to close all nested sections.");
        }
        
        return _details;
    }

    /// <summary>
    /// Helper method to create a simple FindingDetails with just top-level items.
    /// </summary>
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
    /// Helper method to create FindingDetails with items and technical metadata.
    /// </summary>
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

