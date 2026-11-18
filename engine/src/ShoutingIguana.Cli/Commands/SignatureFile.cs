using System.CommandLine;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// <c>--signature-file</c>: a Shopify crawler signature, as the store's admin gives it to copy. Each file holds one
/// signature: its <c>Signature-Input</c> and <c>Signature</c> values, a line each, pasted as values or as header lines
/// (<c>Signature-Input: sig1=…</c>), in either order. <c>Signature-Agent</c> is sent for it; a line of it is allowed and
/// left as it is. A <c>Domain: shop.example</c> line names the host it was made for; without one it is for the crawl's
/// start host. Blank lines and lines starting <c>#</c> are skipped. Nothing read is ever printed: a problem names the
/// line, never what is on it.
/// </summary>
internal static class SignatureFile
{
    public const string OPTION = "--signature-file";

    public const string DESCRIPTION =
        "A Shopify crawler signature, for a store that refuses crawlers without one (429 on product pages). Create it in Shopify " +
        "admin: Online Store > Preferences > Crawler access > Create signature, pick the domain, then Copy the Signature-Input " +
        "and Signature values into a file, a line each (values, or 'Signature-Input: ...' and 'Signature: ...' lines). " +
        "Signature-Agent is sent for you. Add a 'Domain: shop.example' line for another host than the start URL's; each " +
        "domain (www and the bare one too) needs its own signature and file. Repeatable. Sent to that host only, over https, " +
        "and never printed or kept in the crawl file. A new signature can take up to an hour to start working.";

    private const string DOMAIN = "Domain";
    private const string HOST = "Host";

    public static Option<string[]> CreateOption() => new(OPTION) { Description = DESCRIPTION };

    /// <summary>The signatures in <paramref name="paths"/>, each keyed by its host: its Domain line's, else <paramref name="startUrl"/>'s.</summary>
    public static bool TryRead(IReadOnlyList<string> paths, string startUrl, out Dictionary<string, CrawlSignature> signatures, out string error)
    {
        signatures = new Dictionary<string, CrawlSignature>(StringComparer.Ordinal);
        error = string.Empty;
        foreach (var path in paths)
        {
            if (!TryReadOne(path, startUrl, out var host, out var signature, out error))
            {
                return false;
            }

            if (!signatures.TryAdd(host, signature))
            {
                error = $"Two signature files are for {host}: give one.";
                return false;
            }
        }

        return true;
    }

    private static bool TryReadOne(string path, string startUrl, out string host, out CrawlSignature signature, out string error)
    {
        host = string.Empty;
        signature = null!;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"Can't read the signature file {path}: {exception.Message}";
            return false;
        }

        string? input = null, value = null, domain = null;
        for (var number = 1; number <= lines.Length; number++)
        {
            var line = lines[number - 1].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if ((Named(line, DOMAIN) ?? Named(line, HOST)) is { } named)
            {
                domain = named;
            }
            else if (Named(line, CrawlSignature.AGENT_HEADER) is not null)
            {
                // Sent for every signature as Shopify asks; a copy of it in the file changes nothing.
            }
            else if (Named(line, CrawlSignature.INPUT_HEADER) is { } inputLine)
            {
                input = inputLine;
            }
            else if (Named(line, CrawlSignature.SIGNATURE_HEADER) is { } signatureLine)
            {
                value = signatureLine;
            }
            else if (LooksLikeInput(line))
            {
                input = line;
            }
            else if (LooksLikeSignature(line))
            {
                value = line;
            }
            else
            {
                error = $"Line {number} of {path} isn't a Signature-Input or Signature value, or a Domain line. Copy both values from Shopify admin (Online Store > Preferences > Crawler access).";
                return false;
            }
        }

        if (input is null || value is null)
        {
            error = $"{path} has no {(input is null ? "Signature-Input" : "Signature")} value. Copy both from Shopify admin (Online Store > Preferences > Crawler access).";
            return false;
        }

        var hostGiven = domain ?? (Uri.TryCreate(startUrl, UriKind.Absolute, out var start) ? start.Authority : startUrl);
        if (!WebBotSignature.TryNormalizeHost(hostGiven, out host))
        {
            error = $"The signature file {path} is for '{hostGiven}', which isn't a domain.";
            return false;
        }

        signature = new CrawlSignature { SignatureInput = input, Signature = value };
        error = string.Empty;
        return true;
    }

    /// <summary>Which hosts get a signature, and until when: never the values.</summary>
    public static void SayWhatIsSent(IReadOnlyDictionary<string, CrawlSignature> signatures)
    {
        foreach (var (host, signature) in signatures)
        {
            var ends = WebBotSignature.Read(signature.SignatureInput, signature.Signature).Expires is { } expires
                ? $", until {expires.UtcDateTime.ToString("d MMMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UTC"
                : string.Empty;
            Console.WriteLine($"Sending the Shopify signature to {host} only, over https{ends}.");
        }
    }

    /// <summary>The value of a line <c>Name: value</c> (any case), or null when the line is something else.</summary>
    private static string? Named(string line, string name)
    {
        var value = WebBotSignature.Clean(line, name);
        return value.Length == line.Length ? null : value;
    }

    /// <summary>A bare Signature-Input: a lowercase name, then <c>=(</c>, as in <c>sig1=("@authority")</c>.</summary>
    private static bool LooksLikeInput(string line) => line.IndexOf("=(", StringComparison.Ordinal) is > 0 and var at && IsLabel(line[..at]);

    /// <summary>A bare Signature: a lowercase name, then <c>=:</c>, as in <c>sig1=:…:</c>.</summary>
    private static bool LooksLikeSignature(string line) => line.IndexOf("=:", StringComparison.Ordinal) is > 0 and var at && IsLabel(line[..at]);

    private static bool IsLabel(string text) => text.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '-' or '.' or '*');
}
