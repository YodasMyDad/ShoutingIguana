using ShoutingIguana.Engine.Parsing;

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
    public void ExactHashIgnoresCaseAndPunctuation() =>
        Assert.Equal(
            ContentFingerprint.Compute("The quick brown fox").ContentHash,
            ContentFingerprint.Compute("the QUICK, brown... fox!").ContentHash);

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

    private static string Text(int seed, int words)
    {
        var random = new Random(seed);
        return string.Join(' ', Enumerable.Range(0, words).Select(_ => WORDS[random.Next(WORDS.Length)]));
    }
}
