namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Code">Stable, to key on: the engine's <c>SkippedCheckReasons</c>, such as <c>no-sitemap</c>.</param>
/// <param name="Text">A plain sentence to show beside the check, such as "No sitemap could be read."</param>
internal sealed record SkipReasonDto(string Code, string Text);
