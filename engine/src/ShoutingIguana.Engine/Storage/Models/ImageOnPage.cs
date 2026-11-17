namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// An image as a page writes it, for an image an issue names: the first &lt;img&gt; of the page with that URL.
/// </summary>
/// <param name="Width">The width attribute as written ("400", "100%"), null when it has none.</param>
/// <param name="Height">The height attribute as written, null when it has none.</param>
/// <param name="Alt">Its alt text, null when it has none.</param>
/// <param name="HasAlt">It has the alt attribute at all: <c>alt=""</c> does, and is right for a decorative image.</param>
/// <param name="AltLength">The alt text's length as <c>image-alt-too-long</c> measures it; null without one.</param>
/// <param name="Srcset">It has a srcset, so a browser may load a smaller file than this.</param>
/// <param name="Loading">Its loading attribute ("lazy"), null when it has none.</param>
public sealed record ImageOnPage(string? Width, string? Height, string? Alt, bool HasAlt, int? AltLength, bool Srcset, string? Loading);
