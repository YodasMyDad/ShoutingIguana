using ShoutingIguana.Cli.Commands;
using ShoutingIguana.Cli.Output;

namespace ShoutingIguana.Cli.Tests.Output;

public sealed class OutputTests
{
    [Theory]
    [InlineData(0, 5, 7, "05:07")]
    [InlineData(1, 2, 3, "1:02:03")]
    [InlineData(49, 12, 3, "49:12:03")]
    public void DurationsKeepEveryHour(int hours, int minutes, int seconds, string expected) =>
        Assert.Equal(expected, Format.Duration(new TimeSpan(hours, minutes, seconds)));

    [Fact]
    public void CrawledTextCantControlTheTerminal()
    {
        var rightToLeftOverride = (char)0x202E;
        Assert.Equal(@"title\u001B]0;owned\u0007 and \" + "u202Eevil", Format.Printable($"title\u001b]0;owned\u0007 and {rightToLeftOverride}evil"));
        Assert.Equal(@"line\u000Abreak", Format.Printable("line\nbreak"));

        // Every bidirectional control: the isolates and marks reorder text as much as the overrides do.
        foreach (var control in new[] { 0x061C, 0x200E, 0x200F, 0x202A, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069 })
        {
            Assert.Equal($@"a\u{control:X4}b", Format.Printable($"a{(char)control}b"));
        }

        const string plain = "https://example.com/caf%C3%A9 Café 東京";
        Assert.Same(plain, Format.Printable(plain));
        Assert.Equal(string.Empty, Format.Printable(null));
    }

    [Fact]
    public void CsvQuotesFieldsAndDefusesFormulas()
    {
        using var text = new StringWriter();
        var csv = new CsvWriter(text);

        csv.WriteRow("plain", "a,b", "say \"hi\"", "=HYPERLINK(\"http://x\")", "-1", null);

        Assert.Equal("plain,\"a,b\",\"say \"\"hi\"\"\",\"'=HYPERLINK(\"\"http://x\"\")\",'-1,\r\n", text.ToString());
    }

    [Theory]
    [InlineData("https://www.example.com/shop", "www.example.com-")]
    [InlineData("http://[::1]:8080/", "1-")]
    [InlineData("https://xn--bcher-kva.example/", "xn--bcher-kva.example-")]
    public void DefaultCrawlFileNamesAreValidOnEveryFileSystem(string startUrl, string prefix)
    {
        var name = CrawlCommand.DefaultDatabasePath(startUrl);

        Assert.StartsWith(prefix, name);
        Assert.EndsWith(".sicrawl", name);
        Assert.DoesNotContain(name, character => character is ':' or '[' or ']' or '/' or '\\' || char.IsControl(character));
    }
}
