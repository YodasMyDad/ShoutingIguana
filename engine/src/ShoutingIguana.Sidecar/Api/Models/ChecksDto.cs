namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Checks">In catalog order.</param>
internal sealed record ChecksDto(IReadOnlyList<CategoryDto> Categories, IReadOnlyList<CheckDto> Checks);
