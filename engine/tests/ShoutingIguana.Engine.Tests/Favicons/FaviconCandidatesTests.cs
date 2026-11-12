using System.Text.Json;
using ShoutingIguana.Engine.Favicons;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Favicons;

/// <summary>Where a site's icon is looked for, and in which order: the page's own icons, the best for a small tile first, then /favicon.ico.</summary>
public sealed class FaviconCandidatesTests
{
    private static readonly Uri PAGE = new("https://www.example.com/shop/");

    [Fact]
    public void TheSmallestIconOfSixtyFourPixelsOrMoreComesFirstThenTheLargestBelowThenThoseOfNoSize()
    {
        IconFact[] icons =
        [
            Icon("/no-size.ico"),
            Icon("/16.png", "16x16"),
            Icon("/512.png", "512x512"),
            Icon("/32.png", "32x32"),
            Icon("/192.png", "192x192"),
            Icon("/any.png", "any"),
            Icon("/96.png", "96x96")
        ];

        // Three of the page's at most, then where browsers look when a page names none.
        Assert.Equal(["/96.png", "/192.png", "/512.png", "/favicon.ico"], Paths(FaviconCandidates.Rank(PAGE, icons)));
        Assert.Equal(["/32.png", "/16.png", "/no-size.ico", "/favicon.ico"], Paths(FaviconCandidates.Rank(PAGE, [icons[0], icons[1], icons[3], icons[5]])));
        Assert.Equal(["/no-size.ico", "/any.png", "/favicon.ico"], Paths(FaviconCandidates.Rank(PAGE, [icons[0], icons[5]])));
        Assert.Equal(["/favicon.ico"], Paths(FaviconCandidates.Rank(PAGE, [])));
    }

    [Fact]
    public void AnSvgIsNeverACandidateAndAUrlIsTriedOnce()
    {
        IconFact[] icons =
        [
            Icon("/icon.svg", "any", "image/svg+xml"),
            Icon("/vector", null, "IMAGE/SVG+XML"),
            Icon("/logo.SVG"),
            Icon("/packed.svgz?v=2"),
            Icon("/favicon.ico", "48x48"),
            Icon("/favicon.ico"),
            Icon("/icon.png?svg=no", "32x32", "image/png")
        ];
        Assert.Equal(["/favicon.ico", "/icon.png?svg=no"], Paths(FaviconCandidates.Rank(PAGE, icons)));

        // What isn't a URL the crawl could ask for is left out, whatever put it among a page's facts.
        Assert.Equal(["/favicon.ico"], Paths(FaviconCandidates.Rank(PAGE, [new IconFact("data:image/png;base64,AAAA", null, null), new IconFact("not a url", null, null)])));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("any", 0)]
    [InlineData("32x32", 32)]
    [InlineData("48X48", 48)]
    [InlineData("16x16 32x32", 32)]
    [InlineData("16x16 64x64 128x128", 64)]
    [InlineData("256x256 96x96", 96)]
    [InlineData("  72x96\t", 96)]
    [InlineData("big", 0)]
    [InlineData("32", 0)]
    [InlineData("-32x-32", 0)]
    [InlineData("0x0", 0)]
    [InlineData("32x32x32 16x16", 16)]
    public void ASizesAttributeDeclaresTheSizeNearestAboveTheTargetElseItsLargest(string? sizes, int expected) =>
        Assert.Equal(expected, FaviconCandidates.DeclaredSize(sizes));

    [Fact]
    public void APagesIconLinksAreKeptAsFactsInPageOrderResolvedAgainstItsBase()
    {
        const string HTML = """
            <html><head><base href="https://cdn.example.net/assets/">
            <link rel="icon" href="favicon-32.png" sizes="32x32" type="image/png">
            <link rel="shortcut ICON" href="/favicon.ico">
            <link rel="apple-touch-icon" href="/touch.png" sizes="180x180">
            <link rel="icon" href="data:image/png;base64,AAAA">
            <link rel="icon" href="  //static.example.org/i.svg " sizes="any" type=" image/svg+xml ">
            <link rel="mask-icon" href="/mask.svg">
            </head><body><link rel="icon" href="/late.png"></body></html>
            """;
        var parsed = HtmlPageParser.Parse(HTML, PAGE, [], "ShoutingIguana");
        Assert.Equal(
        [
            new IconFact("https://cdn.example.net/assets/favicon-32.png", "32x32", "image/png"),
            new IconFact("https://cdn.example.net/favicon.ico", null, null),
            new IconFact("https://static.example.org/i.svg", "any", "image/svg+xml"),
            new IconFact("https://cdn.example.net/late.png", null, null)
        ], parsed.Facts.Icons);

        // The links are the crawl's as before: an icon and a touch icon are both links of kind icon.
        Assert.Equal(5, parsed.Links.Count(link => link.Kind == LinkKind.Icon));

        // The facts a page is stored with don't hold them: the crawl file is as it was.
        var stored = JsonSerializer.Serialize(new ResponseDetails([], parsed.Facts), EngineJsonContext.Default.ResponseDetails);
        Assert.Contains("titles", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("icon", stored, StringComparison.OrdinalIgnoreCase);
    }

    private static IconFact Icon(string path, string? sizes = null, string? type = null) => new(new Uri(PAGE, path).AbsoluteUri, sizes, type);

    private static IEnumerable<string> Paths(IReadOnlyList<Uri> candidates)
    {
        Assert.All(candidates, candidate => Assert.Equal(PAGE.Host, candidate.Host));
        return candidates.Select(candidate => candidate.PathAndQuery);
    }
}
