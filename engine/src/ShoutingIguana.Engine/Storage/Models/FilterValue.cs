namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A value a filter compares with: text, a number or a boolean, as the column's type needs.</summary>
public readonly record struct FilterValue
{
    public string? Text { get; init; }

    public double? Number { get; init; }

    public bool? Boolean { get; init; }

    public static FilterValue Of(string text) => new() { Text = text };

    public static FilterValue Of(double number) => new() { Number = number };

    public static FilterValue Of(bool boolean) => new() { Boolean = boolean };
}
