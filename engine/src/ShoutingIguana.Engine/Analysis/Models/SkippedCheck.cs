namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>A check the analysis couldn't run on this crawl, and why. Skipped is not the same as finding nothing.</summary>
/// <param name="Reason">A stable code to key on, one of <see cref="SkippedCheckReasons"/>. Stored in crawl files, so never changed.</param>
/// <param name="Text">Why, in a plain sentence to show beside the check, such as "No sitemap could be read."</param>
public sealed record SkippedCheck(string CheckId, string Reason, string Text);
