namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A page's meta refresh: after how long, and to where (null for the page itself).</summary>
public sealed record MetaRefreshInfo(int? DelaySeconds, string? Url);
