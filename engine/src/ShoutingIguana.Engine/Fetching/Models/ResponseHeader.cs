namespace ShoutingIguana.Engine.Fetching.Models;

/// <param name="Name">Lowercase header name.</param>
public readonly record struct ResponseHeader(string Name, string Value);
