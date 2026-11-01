using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Errors.Models;

/// <summary>An <c>application/problem+json</c> body.</summary>
/// <param name="Type">Stable, for the UI to key on: <c>crawl-busy</c>, <c>invalid-options</c>...</param>
/// <param name="Title">A plain sentence the UI shows as it is.</param>
internal sealed record ProblemDto(string Type, string Title, int Status)
{
    /// <summary>Behind "Details" in the UI. Never a stack trace.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    /// <summary>Validation messages keyed by field.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Errors { get; init; }

    /// <summary>For <c>needs-secrets</c>: stored header names whose values must be given again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? MissingHeaders { get; init; }

    /// <summary>For <c>needs-secrets</c>: the proxy's user name and password must be given again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProxyCredentials { get; init; }
}
