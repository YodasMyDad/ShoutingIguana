using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>What a URL is, for the grids, and which of those the rules treat as files rather than pages.</summary>
public sealed class ResourceTypesTests
{
    [Theory]
    [InlineData("text/html", ResourceType.Page)]
    [InlineData("application/xhtml+xml", ResourceType.Page)]
    [InlineData("image/webp", ResourceType.Image)]
    [InlineData("IMAGE/PNG", ResourceType.Image)]
    [InlineData("video/mp4", ResourceType.Media)]
    [InlineData("audio/mpeg", ResourceType.Media)]
    [InlineData("font/woff2", ResourceType.Font)]
    [InlineData("application/vnd.ms-fontobject", ResourceType.Font)]
    [InlineData("text/css; charset=utf-8", ResourceType.Stylesheet)]
    [InlineData("application/javascript", ResourceType.Script)]
    [InlineData("text/javascript", ResourceType.Script)]
    [InlineData("application/pdf", ResourceType.Pdf)]
    [InlineData("application/json", ResourceType.Other)]
    [InlineData("application/xml", ResourceType.Other)]
    [InlineData("", ResourceType.Other)]
    public void AFetchedUrlIsWhatItsContentTypeSays(string contentType, ResourceType expected) =>
        Assert.Equal(expected, ResourceTypes.Classify("https://example.com/photo.jpg", contentType));

    [Theory]
    [InlineData("https://example.com/photo.JPG", ResourceType.Image)]
    [InlineData("https://example.com/clip.webm", ResourceType.Media)]
    [InlineData("https://example.com/site.css?v=3", ResourceType.Stylesheet)]
    [InlineData("https://example.com/app.mjs", ResourceType.Script)]
    [InlineData("https://example.com/fonts/a.woff2", ResourceType.Font)]
    [InlineData("https://example.com/report.pdf", ResourceType.Pdf)]
    [InlineData("https://example.com/download?file=a.jpg", ResourceType.Page)]
    [InlineData("https://example.com/about", ResourceType.Page)]
    [InlineData("https://example.com/feed.xml", ResourceType.Page)]
    public void AUrlThatWasntFetchedIsJudgedByItsExtension(string url, ResourceType expected) =>
        Assert.Equal(expected, ResourceTypes.Classify(url, null));

    /// <summary>The rules' view: images, video, audio, fonts, stylesheets and scripts are files; PDFs and the rest can be indexed.</summary>
    [Fact]
    public void OnlyFilesAreResources()
    {
        Assert.True(ResourceTypes.IsResource("https://example.com/a", "image/png"));
        Assert.True(ResourceTypes.IsResource("https://example.com/a.woff", null));
        Assert.True(ResourceTypes.IsResource("https://example.com/a.js", null));
        Assert.False(ResourceTypes.IsResource("https://example.com/a.pdf", null));
        Assert.False(ResourceTypes.IsResource("https://example.com/a", "application/pdf"));
        Assert.False(ResourceTypes.IsResource("https://example.com/a.jpg", "text/html"));
        Assert.False(ResourceTypes.IsResource("https://example.com/a", "application/json"));
        Assert.All(Enum.GetValues<ResourceType>(), type => Assert.Equal(
            type is ResourceType.Image or ResourceType.Media or ResourceType.Font or ResourceType.Stylesheet or ResourceType.Script,
            ResourceTypes.IsResource("https://example.com/a", type switch
            {
                ResourceType.Page => "text/html",
                ResourceType.Image => "image/gif",
                ResourceType.Script => "application/ecmascript",
                ResourceType.Stylesheet => "text/css",
                ResourceType.Font => "application/x-font-ttf",
                ResourceType.Media => "audio/ogg",
                ResourceType.Pdf => "application/pdf",
                _ => "text/plain"
            })));
    }
}
