namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A check that took points off the score in either crawl, with its points then and now (0 where it took none).</summary>
public sealed record ScoreLineChange(string CheckId, int Then, int Now);
