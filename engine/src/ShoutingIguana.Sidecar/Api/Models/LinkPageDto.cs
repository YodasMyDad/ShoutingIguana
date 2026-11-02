namespace ShoutingIguana.Sidecar.Api.Models;

internal sealed record LinkPageDto(IReadOnlyList<LinkRowDto> Rows, int? Offset, long Total, string? Next);
