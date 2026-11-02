namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How a filter compares. The negative ones (<see cref="Ne"/>, <see cref="NotIn"/>, <see cref="NotContains"/>,
/// <see cref="NotRegex"/>) also match unknown values, as excluding a value should; <see cref="IsNotEmpty"/> leaves those out.
/// </summary>
public enum FilterOperator
{
    Eq,
    Ne,
    Lt,
    Lte,
    Gt,
    Gte,

    /// <summary>Inclusive, with a minimum, a maximum or both.</summary>
    Between,
    In,
    NotIn,

    /// <summary>Ignoring the case of ASCII letters, as SQLite's LIKE does; so do the other two below.</summary>
    Contains,
    NotContains,
    StartsWith,
    EndsWith,

    /// <summary>A .NET regular expression, run without backtracking so that no pattern can hang a query.</summary>
    Regex,
    NotRegex,

    /// <summary>A boolean column is true, or false.</summary>
    Is,

    /// <summary>Unknown (null), and for text also empty.</summary>
    IsEmpty,
    IsNotEmpty
}
