namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record RobotsRecord(string Origin, int? StatusCode, string? Content, string? Error, long FetchedAt);
