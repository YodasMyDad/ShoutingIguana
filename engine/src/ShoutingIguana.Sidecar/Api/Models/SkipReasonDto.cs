namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Code">Stable, to key on: the engine's <c>SkippedCheckReasons</c>, such as <c>no-sitemap</c>.</param>
/// <param name="Text">A plain sentence to show beside the check, such as "No sitemap could be read."</param>
/// <param name="PlainText">Why, in plain words for a reader who isn't an SEO, by the code; null while the code has none.</param>
/// <param name="PlainAction">What would let the check run, in plain words; null with <paramref name="PlainText"/>.</param>
internal sealed record SkipReasonDto(string Code, string Text, string? PlainText, string? PlainAction);
