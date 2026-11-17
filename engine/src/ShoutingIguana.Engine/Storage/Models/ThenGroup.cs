namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One cause of a check in the other crawl: its detail (or fault), its related URL by id there and by text, and its URLs.</summary>
internal sealed record ThenGroup(string? Detail, int? RelatedUrlId, string? RelatedUrl, long Pages);
