namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>The page's <c>href</c> and <c>src</c> values that start <c>//</c>: how many, and the first as written.</summary>
public sealed record ProtocolRelativeFact(int Count, string First);
