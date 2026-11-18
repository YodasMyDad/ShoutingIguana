namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// What <see cref="WebBotSignature.Read"/> found in a signature's two values: its name, when it was made and when it
/// ends (each null when <c>Signature-Input</c> doesn't say), or, when they aren't in the form Shopify gives, why not, in
/// words that never repeat the values.
/// </summary>
public sealed record SignatureReading(string? Label, DateTimeOffset? Created, DateTimeOffset? Expires, string? Problem)
{
    public bool IsValid => Problem is null;

    /// <summary>Whether it has ended by <paramref name="now"/>: one with no end said never has.</summary>
    public bool HasExpired(DateTimeOffset now) => Expires is { } expires && expires <= now;
}
