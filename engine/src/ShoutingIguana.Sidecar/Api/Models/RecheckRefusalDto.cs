namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Why a recheck can't run now: the problem <c>POST …/rechecks</c> would answer with, its type and its title.</summary>
internal sealed record RecheckRefusalDto(string Type, string Title);
