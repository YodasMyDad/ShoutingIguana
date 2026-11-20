using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Reporting;

/// <summary>
/// The report as Simple shows it says what Simple's screens say (the UX audit's S-11). The engine writes the report, so
/// its plain sentences mirror the app's (<c>app/src/simple</c>): <c>PlainRows/plain-rows.json</c> holds what goes in and
/// what the app says, written by the app's own test (<c>app/src/screens/report/plainRows.test.ts</c>), and this holds the
/// engine's mirror to it, case by case. A sentence the app changes fails there, is written to the file, then fails here
/// until the mirror says it too.
/// </summary>
public sealed class PlainRowsTests
{
    private static readonly JsonNode CASES = JsonNode.Parse(RepoFiles.Read("engine", "tests", "ShoutingIguana.Engine.Tests", "Reporting", "PlainRows", "plain-rows.json"))!;

    private static IEnumerable<JsonNode> Of(string kind) => CASES[kind]!.AsArray().Select(each => each!);

    private static T? Enum<T>(JsonNode? value) where T : struct, Enum => value is null ? null : System.Enum.Parse<T>(value.GetValue<string>(), ignoreCase: true);

    /// <summary>What the two said, for the cases that differ: none, when the engine says every one as the app does.</summary>
    private static void AssertSaidAlike(string kind, Func<JsonNode, string?> say, string said = "said")
    {
        var cases = Of(kind).ToList();
        Assert.NotEmpty(cases);
        Assert.Equal(
            [],
            cases.Where(each => say(each) != (string?)each[said]).Select(each => $"{each.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })} but the engine says: {say(each) ?? "null"}"));
    }

    [Fact]
    public void ACheckSDetailIsSaidAsTheAppSaysIt()
    {
        // Only the checks the engine mirrors are in the file; of any other the engine says nothing, and its own detail stands.
        var mirrored = ReportPlainDetails.Mirrored;
        Assert.All(Of("details").Where(each => (string?)each["said"] is not null), each => Assert.Contains((string)each["check"]!, mirrored));
        AssertSaidAlike("details", each => ReportPlainDetails.Say((string)each["check"]!, (string)each["detail"]!));
    }

    [Fact]
    public void APageSEvidenceIsSaidAsTheAppSaysIt()
    {
        AssertSaidAlike("evidence", each => ReportPlainWords.Evidence(
            (string)each["column"]!,
            each["value"]!.GetValueKind() == JsonValueKind.Number ? each["value"]!.GetValue<long>() : (string)each["value"]!,
            (string?)each["origin"],
            (string?)each["url"]));
        AssertSaidAlike("chains", each =>
        {
            List<RedirectHop> hops =
            [
                .. each["hops"]!.AsArray().Select((hop, index) =>
                    new RedirectHop(index + 1, (string)hop!["url"]!, (int?)hop["statusCode"], Enum<UrlOutcome>(hop["outcome"]), RedirectVia.Http))
            ];
            var chain = new RedirectChain(hops, new UrlTarget(hops[^1].Id, hops[^1].Url, hops[^1].StatusCode), (bool)each["loop"]!, (bool)each["tooLong"]!);
            return ReportPlainWords.Evidence(UrlComputedColumns.REDIRECT_CHAIN, chain, (string?)each["origin"]);
        });
        AssertSaidAlike("rules", each => ReportPlainWords.Evidence(
            UrlComputedColumns.ROBOTS_RULE,
            new RobotsRefusal(null, Enum<RobotsBlockReason>(each["reason"])!.Value, (int?)each["line"], (string?)each["rule"]),
            null));
    }

    [Fact]
    public void HowAnAddressAnsweredIsSaidAsTheAppSaysIt()
    {
        AssertSaidAlike("statuses", each => ReportPlainWords.Status((int?)each["statusCode"], Enum<UrlOutcome>(each["outcome"]), (string?)each["error"]));
        AssertSaidAlike("failures", each => ReportPlainWords.FailureWords((string)each["error"]!), "words");
        AssertSaidAlike("failures", each => ReportPlainWords.FetchError((string)each["error"]!), "sentence");
        AssertSaidAlike("notInSearch", each => ReportPlainWords.NotInSearch(Enum<IndexabilityReason>(each["reason"])));
        AssertSaidAlike("redirectKinds", each => ReportPlainWords.RedirectKind((string)each["kinds"]!));
        AssertSaidAlike("fileKinds", each => ReportPlainWords.FileKind((string)each["contentType"]!));
        AssertSaidAlike("seconds", each => ReportPlainWords.Seconds(each["ms"]!.GetValue<double>()));
        AssertSaidAlike("lengths", each => ReportPlainWords.Length(each["seconds"]!.GetValue<long>()));
    }

    [Fact]
    public void WhereALinkIsAndWhatAnImageIsAreSaidAsTheAppSaysThem()
    {
        AssertSaidAlike("positions", each => ReportPlainWords.Position(Enum<LinkPosition>(each["position"])));
        AssertSaidAlike("imageFiles", each => ReportPlainWords.ImageFile((long?)each["sizeBytes"], (int?)each["pixelWidth"], (int?)each["pixelHeight"]));
        AssertSaidAlike("shownAt", each => ReportPlainWords.ShownAt((string?)each["width"], (string?)each["height"]));
        AssertSaidAlike("alts", each => ReportPlainWords.Alt(
            new ImageOnPage(null, null, (string?)each["alt"], (bool)each["hasAlt"]!, (int?)each["altLength"], Srcset: false, Loading: null),
            (bool)each["withLength"]!));
        AssertSaidAlike("others", each => ReportPlainWords.Others((string)each["check"]!, each["count"]!.GetValue<long>()));
    }
}
