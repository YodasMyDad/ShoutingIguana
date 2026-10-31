namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A queued URL claimed for fetching.</summary>
internal sealed record FrontierItem(int Id, string Url, int Depth, FetchMode Mode, bool IsInternal);
