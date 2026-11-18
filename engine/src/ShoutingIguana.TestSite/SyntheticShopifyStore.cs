using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// Keeps <see cref="SyntheticShopify"/> for the server: tells a signed request from an unsigned one, limits the unsigned
/// ones to the product pages' allowance, dresses the store's responses, and counts what arrived where.
/// </summary>
internal sealed partial class SyntheticShopifyStore(SyntheticShopify shopify, TimeProvider timeProvider)
{
    private const string SIGNATURE = "Signature";

    private readonly SyntheticRateLimiter _unsigned = new(
        new SyntheticRateLimit(shopify.UnsignedProductRequests, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(SyntheticShopify.RETRY_AFTER_SECONDS)), timeProvider);

    private long _signed;
    private long _refused;
    private long _signedElsewhere;

    /// <summary>Requests to the store that carried its signature, valid or not.</summary>
    public long Signed => Interlocked.Read(ref _signed);

    /// <summary>Product requests answered 429.</summary>
    public long Refused => Interlocked.Read(ref _refused);

    /// <summary>Requests that carried a signature to anywhere but the store under its own name: the external site, or the store as <c>localhost</c>.</summary>
    public long SignedElsewhere => Interlocked.Read(ref _signedElsewhere);

    /// <summary>Notes a request to the external site, which no signature may reach.</summary>
    public void SawExternal(HttpRequest request)
    {
        if (request.Headers.ContainsKey(SIGNATURE))
        {
            Interlocked.Increment(ref _signedElsewhere);
        }
    }

    /// <summary>The store's answer in place of the site's, or null to let the site answer: 429 for a product it refuses.</summary>
    public SyntheticResponse? Refuse(HttpRequest request, string siteAuthority)
    {
        var host = request.Host.Value ?? string.Empty;
        var isTheStore = host.Equals(siteAuthority, StringComparison.OrdinalIgnoreCase);
        if (request.Headers.ContainsKey(SIGNATURE) && isTheStore)
        {
            Interlocked.Increment(ref _signed);
        }
        else if (request.Headers.ContainsKey(SIGNATURE))
        {
            Interlocked.Increment(ref _signedElsewhere);
        }

        var path = request.Path.Value ?? "/";
        if (!path.StartsWith("/p/", StringComparison.Ordinal) || IsValidlySigned(request, host, siteAuthority) || !_unsigned.IsOverTheLimit(path))
        {
            return null;
        }

        Interlocked.Increment(ref _refused);
        return new SyntheticResponse(429, "text/html; charset=utf-8", "<html><body>Too many requests</body></html>", null, null,
            new Dictionary<string, string>
            {
                ["Retry-After"] = SyntheticShopify.RETRY_AFTER_SECONDS.ToString(CultureInfo.InvariantCulture),
                ["powered-by"] = "Shopify"
            });
    }

    /// <summary>
    /// The store's headers on every response of the site's port, and on its home page the theme object and two ways off
    /// the site over https, to the same server named <c>localhost</c>: a link, and <see cref="SyntheticShopify.AWAY_PATH"/>.
    /// </summary>
    public SyntheticResponse Dress(SyntheticResponse response, string path, int sitePort)
    {
        var headers = new Dictionary<string, string>(response.Headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["powered-by"] = "Shopify",
            ["shopify-complexity-score"] = "1700"
        };

        var text = response.Text;
        if (path == "/" && text is not null && response.StatusCode == 200)
        {
            var extra = """<script>var Shopify = Shopify || {}; Shopify.theme = {"name":"Synthetic","id":1};</script>""" +
                        $"""<a href="{SyntheticShopify.AWAY_PATH}">Gift cards</a><a href="https://localhost:{sitePort}/p/1">Our other shop</a>""";
            text = text.Replace("</body>", $"{extra}</body>", StringComparison.Ordinal);
        }

        return response with { Text = text, Headers = headers };
    }

    /// <summary>The redirect off the store, to the same server under another host's name.</summary>
    public static SyntheticResponse Away(int sitePort) =>
        new(301, "text/html; charset=utf-8", null, null, $"https://localhost:{sitePort}/p/2");

    /// <summary>
    /// The store's one signature, to the host it was made for, before it expires, with Shopify's agent. Its expiry is read
    /// from the request's Signature-Input, so the expired copy of the signature is refused.
    /// </summary>
    private bool IsValidlySigned(HttpRequest request, string host, string siteAuthority)
    {
        var input = request.Headers["Signature-Input"].ToString();
        var expires = ExpiresPattern().Match(input);
        return request.Headers[SIGNATURE].ToString() == SyntheticShopify.SIGNATURE &&
               request.Headers["Signature-Agent"].ToString() == SyntheticShopify.AGENT &&
               input.Contains($"keyid=\"{SyntheticShopify.KEY_ID}\"", StringComparison.Ordinal) &&
               expires.Success && long.Parse(expires.Groups[1].Value, CultureInfo.InvariantCulture) > timeProvider.GetUtcNow().ToUnixTimeSeconds() &&
               host.Equals(shopify.SignatureFor ?? siteAuthority, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@";expires=(\d{1,15})(?:;|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ExpiresPattern();
}
