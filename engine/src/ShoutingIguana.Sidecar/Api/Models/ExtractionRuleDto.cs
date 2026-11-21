using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One custom extraction rule, as a crawl's options carry it. A property left out keeps the engine's default.</summary>
internal sealed record ExtractionRuleDto
{
    public string? Name { get; init; }
    public ExtractionSelector? SelectorType { get; init; }
    public string? Selector { get; init; }

    /// <summary>Left out, the rule is on.</summary>
    public bool? Enabled { get; init; }

    public static ExtractionRuleDto From(ExtractionRule rule) => new()
    {
        Name = rule.Name,
        SelectorType = rule.SelectorType,
        Selector = rule.Selector,
        Enabled = rule.Enabled
    };

    public ExtractionRule ToRule() => new()
    {
        Name = Name?.Trim() ?? "",
        SelectorType = SelectorType ?? ExtractionSelector.Css,
        Selector = Selector?.Trim() ?? "",
        Enabled = Enabled ?? true
    };
}
