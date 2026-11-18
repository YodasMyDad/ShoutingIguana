namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// A crawler signature (Web Bot Auth) for one host, as Shopify's admin gives it under Online Store › Preferences ›
/// Crawler access: two fixed header values, copied as they are, sent with every request to that host only
/// (<see cref="Fetching.SignatureBook"/>), with <c>Signature-Agent: "https://shopify.com"</c> beside them. Nothing is
/// computed from them. They are a secret: a crawl file never keeps them (<see cref="CrawlOptions.WithoutSecrets"/>), and
/// this record never prints them.
/// </summary>
public sealed record CrawlSignature
{
    public const string INPUT_HEADER = "Signature-Input";
    public const string SIGNATURE_HEADER = "Signature";
    public const string AGENT_HEADER = "Signature-Agent";

    /// <summary>The <c>Signature-Agent</c> value Shopify asks for, the quotes part of it.</summary>
    public const string SHOPIFY_AGENT = "\"https://shopify.com\"";

    /// <summary>The <c>Signature-Input</c> header's value: the signature's name, what it signs, and when it was made and ends.</summary>
    public required string SignatureInput { get; init; }

    /// <summary>The <c>Signature</c> header's value: the signature's name, then the signature itself between colons.</summary>
    public required string Signature { get; init; }

    /// <summary>Never the values: a record would print them, and they end up in logs that way.</summary>
    public override string ToString() => $"{nameof(CrawlSignature)} {{ values hidden }}";
}
