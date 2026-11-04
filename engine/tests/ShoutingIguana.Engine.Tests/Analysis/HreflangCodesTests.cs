using ShoutingIguana.Engine.Analysis.Rules;

namespace ShoutingIguana.Engine.Tests.Analysis;

public sealed class HreflangCodesTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("pt-BR")]
    [InlineData("PT-br")]
    [InlineData("zh-Hant")]
    [InlineData("zh-Hant-TW")]
    [InlineData("sr-RS")]
    [InlineData("nb-NO")]
    [InlineData("x-default")]
    public void AcceptsValidCodes(string code) => Assert.True(HreflangCodes.IsValid(code));

    [Theory]
    [InlineData("en-UK")]
    [InlineData("es-419")]
    [InlineData("en_GB")]
    [InlineData("uk-UA-extra-part")]
    [InlineData("english")]
    [InlineData("eng")]
    [InlineData("gb")]
    [InlineData("en-")]
    [InlineData("")]
    public void RejectsInvalidCodes(string code) => Assert.False(HreflangCodes.IsValid(code));
}
