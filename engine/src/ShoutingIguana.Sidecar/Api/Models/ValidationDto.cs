namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Errors">Messages keyed by option field. Empty when the options are valid.</param>
internal sealed record ValidationDto(IReadOnlyDictionary<string, IReadOnlyList<string>> Errors);
