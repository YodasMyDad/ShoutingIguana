using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Parsing;

public sealed class ContentFingerprintTests
{
    private static readonly string[] WORDS =
    [
        "garden", "kettle", "river", "copper", "lantern", "meadow", "harbour", "velvet", "orchard", "pepper",
        "anchor", "biscuit", "candle", "dagger", "ember", "falcon", "glacier", "hammer", "island", "jacket"
    ];

    [Fact]
    public void CountsWords() => Assert.Equal(4, ContentFingerprint.Compute("Hello, World! hello world").WordCount);

    [Fact]
    public void ExactHashIgnoresCaseAndPunctuation()
    {
        Assert.Equal(
            ContentFingerprint.Compute("The quick brown fox").ContentHash,
            ContentFingerprint.Compute("the QUICK, brown... fox!").ContentHash);
        Assert.NotEqual(
            ContentFingerprint.Compute("The quick brown fox").ContentHash,
            ContentFingerprint.Compute("The quick brown dog").ContentHash);
    }

    [Fact]
    public void FingerprintsOfPlainTextDontChange()
    {
        // Values from before word joining was added, lane 0 from before the fingerprint had four lanes: stored crawls compare
        // against them, and a crawl continued by a later engine compares its new pages with the lanes it stored.
        var result = ContentFingerprint.Compute("Garden kettles, copper lanterns and 12 velvet pillows for every season.");

        Assert.Equal((11, 18091267895285211189UL, 8841941512683386144UL), (result.WordCount, result.ContentHash, result.SimHash));
        Assert.Equal(new SimHashLanes(1917921518557425498UL, 4227384739695081441UL, 14753103772142005898UL), result.Lanes);
    }

    [Theory]
    [InlineData("नमस्ते दुनिया", 2)] // Hindi vowel signs and the virama are combining marks inside the words
    [InlineData("cafés crème", 2)] // decomposed accents
    [InlineData("don't stop, O’Brien’s 90's", 4)]
    [InlineData("rock 'n' roll, the students' books", 6)]
    [InlineData("co\u00ADoperate", 1)] // soft hyphen
    [InlineData("می\u200Cخواهم", 1)] // Persian with a zero-width non-joiner
    public void MarksJoinersAndApostrophesStayInsideWords(string text, int words) =>
        Assert.Equal(words, ContentFingerprint.Compute(text).WordCount);

    [Fact]
    public void InvisibleJoinersAndApostropheStylesDontChangeTheHash()
    {
        Assert.Equal(ContentFingerprint.Compute("don't cooperate").ContentHash, ContentFingerprint.Compute("don’t co\u00ADoperate").ContentHash);
        Assert.NotEqual(ContentFingerprint.Compute("don't").ContentHash, ContentFingerprint.Compute("don t").ContentHash);
    }

    [Fact]
    public void NearDuplicatesHaveCloseSimHashes()
    {
        var original = Text(seed: 1, words: 300);
        var words = original.Split(' ');
        words[150] = "ocean";
        var edited = string.Join(' ', words);
        var unrelated = Text(seed: 2, words: 300);

        var distanceToEdit = ContentFingerprint.Distance(ContentFingerprint.Compute(original).SimHash, ContentFingerprint.Compute(edited).SimHash);
        var distanceToOther = ContentFingerprint.Distance(ContentFingerprint.Compute(original).SimHash, ContentFingerprint.Compute(unrelated).SimHash);

        Assert.InRange(distanceToEdit, 0, 8);
        Assert.True(distanceToOther > 12, $"Unrelated text was only {distanceToOther} bits away");
    }

    [Fact]
    public void EmptyTextHasNoFingerprint() => Assert.Equal(default, ContentFingerprint.Compute(string.Empty));

    [Fact]
    public void UnspacedScriptsCountEachCharacterAsAWord()
    {
        Assert.Equal(7, ContentFingerprint.Compute("我们的产品很好").WordCount);
        Assert.Equal(5, ContentFingerprint.Compute("新しい shoes 靴").WordCount);
        Assert.Equal(2, ContentFingerprint.Compute("hello world").WordCount);
    }

    private static string Text(int seed, int words)
    {
        var random = new Random(seed);
        return string.Join(' ', Enumerable.Range(0, words).Select(_ => WORDS[random.Next(WORDS.Length)]));
    }
}
