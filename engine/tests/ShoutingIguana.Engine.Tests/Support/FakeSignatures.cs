using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Crawler signatures in the form Shopify's admin gives them, made up for tests: the key id and the signature say so, and
/// no real store would take them. Every check that a signature never leaks looks for <see cref="MARKER"/>, which both
/// values hold.
/// </summary>
internal static class FakeSignatures
{
    /// <summary>In both values, so a leak of either is found by one search.</summary>
    public const string MARKER = "FAKETESTONLY";

    /// <summary>1 Jan 2026, 00:00 UTC.</summary>
    public const long CREATED = 1_767_225_600;

    /// <summary>1 Jan 2100, 00:00 UTC: far from expiring.</summary>
    public const long LASTS_UNTIL = 4_102_444_800;

    /// <summary>64 bytes in base64, as an Ed25519 signature is, that start with <see cref="MARKER"/> and are nothing else.</summary>
    public const string SIGNATURE = $"sig1=:{MARKER}AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==:";

    public static string Input(long created = CREATED, long expires = LASTS_UNTIL, string label = "sig1") =>
        $"{label}=(\"@authority\" \"signature-agent\");created={created};expires={expires};keyid=\"{MARKER}-key\";alg=\"ed25519\";tag=\"web-bot-auth\"";

    public static CrawlSignature Valid(long expires = LASTS_UNTIL) => new() { SignatureInput = Input(expires: expires), Signature = SIGNATURE };

    public static Dictionary<string, CrawlSignature> For(string host, long expires = LASTS_UNTIL) => new() { [host] = Valid(expires) };
}
