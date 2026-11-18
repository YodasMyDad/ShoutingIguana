using System.Globalization;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Reads a crawler signature (Web Bot Auth, RFC 9421's headers) as Shopify's admin gives it: enough to tell that its two
/// values are in that form, that they belong together, and when it was made and ends, which is what the crawl needs to
/// refuse an expired one and to say when it ends. The values are sent as they are: nothing here computes or checks a
/// signature. No problem it describes ever repeats a value, since the values are a secret.
/// </summary>
public static class WebBotSignature
{
    private const string INPUT_FORM = "Signature-Input isn't in the form Shopify gives: a name, then what it signs in brackets, then " +
                                      "its times, as in sig1=(\"@authority\" \"signature-agent\");created=…;expires=…";

    private const string SIGNATURE_FORM = "Signature isn't in the form Shopify gives: a name, then the signature between colons, as in sig1=:…:";

    private const string ONE_ONLY = "copy the one Shopify shows for this domain.";

    public static SignatureReading Read(string? signatureInput, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signatureInput))
        {
            return Problem("Signature-Input is empty.");
        }

        if (!StructuredFields.TryParseDictionary(signatureInput, out var inputs))
        {
            return Problem($"{INPUT_FORM}.");
        }

        if (inputs.Count > 1)
        {
            return Problem($"Signature-Input holds more than one signature: {ONE_ONLY}");
        }

        var input = inputs[0];
        if (input.ItemKind != StructuredItemKind.InnerList)
        {
            return Problem($"{INPUT_FORM}.");
        }

        if (!TryReadTime(input, "created", out var created))
        {
            return Problem("Signature-Input's created= isn't a time.");
        }

        if (!TryReadTime(input, "expires", out var expires))
        {
            return Problem("Signature-Input's expires= isn't a time.");
        }

        if (created is not null && expires is not null && expires <= created)
        {
            return Problem("Signature-Input ends before it starts.");
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return Problem("Signature is empty.");
        }

        if (!StructuredFields.TryParseDictionary(signature, out var signatures))
        {
            return Problem($"{SIGNATURE_FORM}.");
        }

        if (signatures.Count > 1)
        {
            return Problem($"Signature holds more than one signature: {ONE_ONLY}");
        }

        if (signatures[0] is not { ItemKind: StructuredItemKind.ByteSequence, Item.Length: > 0 } signed)
        {
            return Problem($"{SIGNATURE_FORM}.");
        }

        if (signed.Key != input.Key)
        {
            return Problem($"Signature-Input and Signature are for different signatures ({input.Key} and {signed.Key}): copy both from the same signature in Shopify admin.");
        }

        return new SignatureReading(input.Key, created, expires, null);
    }

    /// <summary>
    /// A value as pasted, ready to send: trimmed, and without its header's name in front when a whole header line was
    /// copied (<c>Signature-Input: sig1=…</c>). Another header's name stays: <c>Signature-Input: …</c> is no Signature.
    /// </summary>
    public static string Clean(string text, string headerName)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith(headerName, StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[headerName.Length..].TrimStart(' ', '\t');
            if (rest.StartsWith(':'))
            {
                return rest[1..].Trim();
            }
        }

        return trimmed;
    }

    /// <summary>
    /// The host a signature is for, as its requests' authority: lowercase, in punycode, without a trailing dot, with the
    /// port only off the default (<c>shop.example</c>, <c>127.0.0.1:5001</c>). It may be given as a domain or as an
    /// address with nothing after the host (<c>https://shop.example/</c>); a path, a query or a user name makes it no host.
    /// </summary>
    public static bool TryNormalizeHost(string? text, out string authority)
    {
        authority = string.Empty;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var address = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Host.Length == 0)
        {
            return false;
        }

        authority = AuthorityOf(uri);
        return true;
    }

    /// <summary>A request's authority as <see cref="TryNormalizeHost"/> names hosts, which is what a signature is looked up by.</summary>
    public static string AuthorityOf(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>A parameter given as seconds since 1970, as RFC 9421 has <c>created</c> and <c>expires</c>; absent is null and fine.</summary>
    private static bool TryReadTime(StructuredMember member, string name, out DateTimeOffset? time)
    {
        time = null;
        if (!member.Parameters.TryGetValue(name, out var parameter))
        {
            return true;
        }

        if (parameter.Kind != StructuredItemKind.Integer ||
            !long.TryParse(parameter.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        time = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private static SignatureReading Problem(string problem) => new(null, null, null, problem);
}
