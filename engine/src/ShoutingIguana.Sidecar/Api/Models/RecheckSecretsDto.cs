namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What a recheck would ask for, as Continue does (<c>needs-secrets</c>'s fields): the header values and proxy credentials the file doesn't keep.</summary>
internal sealed record RecheckSecretsDto(IReadOnlyList<string> MissingHeaders, bool ProxyCredentials, bool Auth = false);
