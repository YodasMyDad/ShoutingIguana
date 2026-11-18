namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// What the crawl's signatures had to do with a request, so a refusal can say which problem it is when that can be told.
/// <see cref="Host"/> is the host the request went to, <see cref="SignedHost"/> the one the signature is for (another
/// for <see cref="SignatureSituationKind.OtherHost"/>), and <see cref="At"/> when the signature was made
/// (<see cref="SignatureSituationKind.Signed"/>) or ended (<see cref="SignatureSituationKind.Expired"/>), when its
/// Signature-Input says.
/// </summary>
public sealed record SignatureSituation(SignatureSituationKind Kind, string Host, string? SignedHost = null, DateTimeOffset? At = null)
{
    public static SignatureSituation None(string host) => new(SignatureSituationKind.None, host);
}
