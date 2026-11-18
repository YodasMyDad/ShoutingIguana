using ShoutingIguana.Cli.Commands;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;
using static ShoutingIguana.Cli.Tests.CliTests;

namespace ShoutingIguana.Cli.Tests;

/// <summary>
/// <c>--signature-file</c>: a Shopify crawler signature read as Shopify's admin gives it to copy, for crawl and recheck
/// alike, never printed, kept or exported. The values here are the synthetic store's, made up.
/// </summary>
public sealed class SignatureFileTests : IAsyncLifetime
{
    private static readonly string HEADER_LINES = $"Signature-Input: {SyntheticShopify.SIGNATURE_INPUT}\nSignature: {SyntheticShopify.SIGNATURE}\n";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"si-signature-{Guid.NewGuid():N}");
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 10 });
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task TheHelpSaysWhereASignatureComesFrom()
    {
        foreach (var command in new[] { "crawl", "recheck" })
        {
            var help = await RunAsync(command, "--help");

            Assert.Contains("--signature-file", help.Output);
            Assert.Contains("Online Store > Preferences > Crawler access > Create signature", help.Output);
        }
    }

    [Theory]
    [InlineData("header lines")]
    [InlineData("bare values")]
    [InlineData("bare values, signature first")]
    [InlineData("with Shopify's agent and notes")]
    public void ReadsTheValuesAsShopifyGivesThemToCopy(string form)
    {
        var text = form switch
        {
            "header lines" => HEADER_LINES,
            "bare values" => $"{SyntheticShopify.SIGNATURE_INPUT}\r\n{SyntheticShopify.SIGNATURE}\r\n",
            "bare values, signature first" => $"\n  {SyntheticShopify.SIGNATURE}  \n\n{SyntheticShopify.SIGNATURE_INPUT}",
            _ => $"# From Shopify admin\nsignature-input:{SyntheticShopify.SIGNATURE_INPUT}\nSignature-Agent: \"https://shopify.com\"\nSIGNATURE: {SyntheticShopify.SIGNATURE}\n"
        };

        Assert.True(SignatureFile.TryRead([Write(text)], "https://Shop.Example/products", out var signatures, out var error), error);

        var signature = Assert.Single(signatures);
        Assert.Equal("shop.example", signature.Key);
        Assert.Equal(SyntheticShopify.SIGNATURE_INPUT, signature.Value.SignatureInput);
        Assert.Equal(SyntheticShopify.SIGNATURE, signature.Value.Signature);
    }

    [Fact]
    public void ADomainLineTiesItToThatHost()
    {
        var file = Write($"Domain: www.shop.example\n{HEADER_LINES}");

        Assert.True(SignatureFile.TryRead([file], "https://shop.example/", out var signatures, out var error), error);

        Assert.Equal("www.shop.example", Assert.Single(signatures).Key);
    }

    [Fact]
    public void ProblemsNameTheLineNeverTheValues()
    {
        var unknown = Write($"Signature-Input: {SyntheticShopify.SIGNATURE_INPUT}\n{SyntheticShopify.KEY_ID} pasted wrongly\n");
        var half = Write($"Signature-Input: {SyntheticShopify.SIGNATURE_INPUT}\n");
        var twice = Write(HEADER_LINES);

        Assert.False(SignatureFile.TryRead([unknown], "https://shop.example/", out _, out var unknownError));
        Assert.False(SignatureFile.TryRead([half], "https://shop.example/", out _, out var halfError));
        Assert.False(SignatureFile.TryRead([twice, twice], "https://shop.example/", out _, out var twiceError));
        Assert.False(SignatureFile.TryRead([Path.Combine(_folder, "missing.txt")], "https://shop.example/", out _, out var missingError));

        Assert.StartsWith("Line 2 of ", unknownError);
        Assert.EndsWith("has no Signature value. Copy both from Shopify admin (Online Store > Preferences > Crawler access).", halfError);
        Assert.Equal("Two signature files are for shop.example: give one.", twiceError);
        Assert.StartsWith("Can't read the signature file", missingError);
        Assert.All([unknownError, halfError, twiceError], error => Assert.DoesNotContain(SyntheticShopify.KEY_ID, error));
    }

    [Fact]
    public async Task AnExpiredSignatureIsRefusedByCrawlAndRecheck()
    {
        var expired = Write($"Signature-Input: {SyntheticShopify.EXPIRED_SIGNATURE_INPUT}\nSignature: {SyntheticShopify.SIGNATURE}\n");
        var crawl = Path.Combine(_folder, "expired.sicrawl");

        AssertFails(await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--signature-file", expired, "-q", "--no-analyze"),
            $"The Shopify signature for {_server.BaseUri.Authority} expired on 1 January 2026.");
        Assert.False(File.Exists(crawl));

        Assert.Equal(0, (await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--max-urls", "15", "-q")).ExitCode);
        AssertFails(await RunAsync("recheck", crawl, "--check", "title-too-long", "--signature-file", expired), "expired on 1 January 2026");
    }

    [Fact]
    public async Task ASignatureIsNeverPrintedKeptExportedOrSentOverPlainHttp()
    {
        var file = Write(HEADER_LINES);
        var crawl = Path.Combine(_folder, "signed.sicrawl");
        var csv = Path.Combine(_folder, "urls.csv");
        var sent = _server.Requests.Watch("Signature");

        var crawled = await RunAsync("crawl", _server.BaseUri.AbsoluteUri, "--db", crawl, "--max-urls", "15", "--signature-file", file);
        var rechecked = await RunAsync("recheck", crawl, "--check", "title-too-long", "--signature-file", file);
        var exported = await RunAsync("export", crawl, "--kind", "workbook", "--out", Path.Combine(_folder, "all.xlsx"));
        var listed = await RunAsync("export", crawl, "--kind", "urls", "--out", csv);

        Assert.True(crawled.ExitCode == 0, crawled.Error);
        Assert.True(rechecked.ExitCode == 0, rechecked.Error);
        Assert.Equal(0, exported.ExitCode);
        Assert.Equal(0, listed.ExitCode);
        Assert.Contains($"Sending the Shopify signature to {_server.BaseUri.Authority} only, over https, until 1 January 2100 00:00 UTC.", crawled.Output);
        Assert.Empty(CrawlDatabase.ReadOptions(crawl).Signatures);
        foreach (var text in new[] { crawled.Output, crawled.Error, rechecked.Output, rechecked.Error, await File.ReadAllTextAsync(csv) })
        {
            Assert.DoesNotContain(SyntheticShopify.KEY_ID, text);
            Assert.DoesNotContain(SyntheticShopify.SIGNATURE, text);
        }

        Assert.True((await File.ReadAllBytesAsync(crawl)).AsSpan().IndexOf("synthetic-store-fake-key"u8) < 0);

        // The site is plain http: a signature never goes there.
        Assert.Empty(sent);
    }

    private string Write(string text)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        return path;
    }
}
