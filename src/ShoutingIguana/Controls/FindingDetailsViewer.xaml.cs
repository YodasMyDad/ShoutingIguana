using System.Windows.Controls;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Controls;

/// <summary>
/// Control for displaying hierarchical FindingDetails.
/// </summary>
public partial class FindingDetailsViewer : UserControl
{
    public FindingDetailsViewer()
    {
        InitializeComponent();
    }
    
    /// <summary>
    /// Dependency property for FindingDetails.
    /// </summary>
    public static readonly System.Windows.DependencyProperty DetailsProperty =
        System.Windows.DependencyProperty.Register(
            nameof(Details),
            typeof(FindingDetails),
            typeof(FindingDetailsViewer),
            new System.Windows.PropertyMetadata(null));
    
    /// <summary>
    /// The FindingDetails to display.
    /// </summary>
    public FindingDetails? Details
    {
        get => (FindingDetails?)GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }
}

