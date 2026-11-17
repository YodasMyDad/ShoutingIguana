namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The order a page of link groups is in, as the request's sort key names it.</summary>
internal sealed record LinkGroupSortDto(string Column, bool Desc);
