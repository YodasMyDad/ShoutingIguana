namespace ShoutingIguana.TestSite.Models;

public sealed record SyntheticResponse(int StatusCode, string ContentType, string? Text, byte[]? Bytes, string? Location);
