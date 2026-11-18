using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A Shopify crawler signature's two values, as the store's admin gives them to copy (<see cref="CrawlSignature"/>).
/// Only ever in requests: no response gives one back, and no crawl file or saved default keeps one.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CrawlSignatureDto
{
    public string? SignatureInput { get; init; }
    public string? Signature { get; init; }

    /// <summary>The engine's signatures by host, each value as given (a missing one is empty, which validation refuses).</summary>
    public static IReadOnlyDictionary<string, CrawlSignature> ToEngine(IReadOnlyDictionary<string, CrawlSignatureDto>? given) =>
        (given ?? new Dictionary<string, CrawlSignatureDto>()).ToDictionary(
            signature => signature.Key,
            signature => new CrawlSignature { SignatureInput = signature.Value?.SignatureInput ?? string.Empty, Signature = signature.Value?.Signature ?? string.Empty },
            StringComparer.Ordinal);

    /// <summary>Never the values, though a record would print them.</summary>
    public override string ToString() => $"{nameof(CrawlSignatureDto)} {{ values hidden }}";
}
