using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// JavaScript rendering and the include/exclude patterns that keep a rendered crawl out of a filter trap.
/// </summary>
public sealed class JavaScriptRenderingTests
{
    /// <summary>A full page, not a shell. The script deletes the raw link and adds one the raw HTML does not have.</summary>
    private const string FULL = """
        <!DOCTYPE html><html><head><title>Catalogue</title></head><body>
        <h1>Catalogue</h1>
        <p>A long catalogue page that already has its text, so automatic rendering leaves it as it was sent. A long catalogue page that already has its text.</p>
        <a href="/in-the-html">Already here</a>
        <script>
        document.querySelector('a').remove();
        var link = document.createElement('a');
        link.setAttribute('href', '/from-' + 'js');
        link.textContent = 'Added by the page';
        document.body.appendChild(link);
        </script>
        </body></html>
        """;

    private const string SHELL = """
        <!DOCTYPE html><html><head><title>Shop</title></head><body>
        <div id="app"></div>
        <script>
        document.body.innerHTML = '<h1>Shop</h1><p>The catalogue is written by the page.</p>';
        var link = document.createElement('a');
        link.setAttribute('href', '/rendered-' + 'only');
        link.textContent = 'The rendered product';
        document.body.appendChild(link);
        var img = document.createElement('img');
        img.setAttribute('src', '/photo.jpg');
        img.setAttribute('width', '200');
        img.setAttribute('height', '200');
        document.body.appendChild(img);
        </script>
        </body></html>
        """;

    [Fact]
    public void AShellNeedsRenderingAndAPageThatAlreadyHasItsTextDoesNot()
    {
        Assert.True(RenderNeed.IsClientShell(SHELL));
        Assert.DoesNotContain("/rendered-only", SHELL, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", SHELL, StringComparison.Ordinal);

        var page = "<!DOCTYPE html><html><head><script src=\"/app.js\"></script><title>A real page</title></head><body><h1>A real page</h1><p>" +
                   new string('a', 200) + "</p></body></html>";
        Assert.False(RenderNeed.IsClientShell(page));
        Assert.False(RenderNeed.ShouldRender(JavaScriptRendering.Off, UrlPatterns.None, "https://example.com/", SHELL));
        Assert.True(RenderNeed.ShouldRender(JavaScriptRendering.Automatic, UrlPatterns.None, "https://example.com/", SHELL));
        Assert.False(RenderNeed.ShouldRender(JavaScriptRendering.Automatic, UrlPatterns.None, "https://example.com/", page));
        Assert.True(RenderNeed.ShouldRender(JavaScriptRendering.JavaScript, UrlPatterns.None, "https://example.com/", page));
    }

    [Fact]
    public void IncludeAndExcludeDecideWhatIsFetchedAndABadPatternIsRefused()
    {
        var options = new CrawlOptions
        {
            StartUrl = "https://example.com/",
            IncludePatterns = ["/catalogue/"],
            ExcludePatterns = ["sort="]
        };
        var filter = new UrlPatternFilter(options);
        Assert.True(filter.Allows("https://example.com/catalogue/chair"));
        Assert.False(filter.Allows("https://example.com/catalogue/chair?sort=price"));
        Assert.False(filter.Allows("https://example.com/about"));

        var refused = new CrawlOptions { StartUrl = "https://example.com/", ExcludePatterns = ["example"] }.ValidateFields();
        Assert.Contains("excludePatterns", refused.Keys);

        var notAPattern = new CrawlOptions { StartUrl = "https://example.com/", IncludePatterns = ["("] }.Validate();
        Assert.Contains(notAPattern, message => message.Contains("not a regular expression", StringComparison.Ordinal));
    }

    [Fact]
    public void ACrawlFileFromBeforeRenderingReadsAsOff()
    {
        var options = JsonSerializer.Deserialize("""{"startUrl":"https://example.com/"}""", EngineJsonContext.Default.CrawlOptions);
        Assert.NotNull(options);
        Assert.Equal(JavaScriptRendering.Off, options.Rendering);
        Assert.Empty(options.IncludePatterns ?? []);
    }

    [Fact]
    public async Task ACrawlFindsTheLinkAndTheIssueTheRawHtmlDoesNotHave()
    {
        await using var site = await TinySite.StartAsync();
        var rendered = await CrawlAsync(site, new CrawlOptions { StartUrl = site.Root, DiscoverSitemaps = false, CheckResources = false, CheckExternalLinks = false });

        Assert.Equal(1, Count(rendered, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}rendered-only'"));
        Assert.Equal(1, Count(rendered, $"""
            SELECT COUNT(*) FROM links l
            JOIN urls f ON f.id = l.from_id
            JOIN urls t ON t.id = l.to_id
            WHERE f.url = '{site.Root}' AND t.url = '{site.Root}rendered-only'
            """));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(rendered);
        Assert.Equal(1, Count(rendered, $"""
            SELECT COUNT(*) FROM issues i JOIN urls u ON u.id = i.url_id
            WHERE i.issue = 'image-missing-alt' AND u.url = '{site.Root}'
            """));

        var plain = await CrawlAsync(site, new CrawlOptions
        {
            StartUrl = site.Root,
            Rendering = JavaScriptRendering.Off,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false
        });
        Assert.Equal(0, Count(plain, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}rendered-only'"));
    }

    [Fact]
    public async Task JavaScriptModeRendersAFullPageAndKeepsALinkTheScriptRemoved()
    {
        await using var site = await TinySite.StartAsync();
        var options = new CrawlOptions
        {
            StartUrl = site.Root + "full",
            Rendering = JavaScriptRendering.JavaScript,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false,
            RespectRobotsTxt = false
        };
        var rendered = await CrawlAsync(site, options);
        Assert.Equal(1, Count(rendered, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}from-js'"));
        Assert.Equal(1, Count(rendered, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}in-the-html'"));

        var automatic = await CrawlAsync(site, options with { Rendering = JavaScriptRendering.Automatic });
        Assert.Equal(0, Count(automatic, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}from-js'"));
        Assert.Equal(1, Count(automatic, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}in-the-html'"));
    }

    [Fact]
    public async Task AnExcludePatternKeepsACrawlOutOfAFilterTrap()
    {
        await using var site = await TinySite.StartAsync();
        var path = await CrawlAsync(site, new CrawlOptions
        {
            StartUrl = site.Root + "links",
            Rendering = JavaScriptRendering.Off,
            ExcludePatterns = ["sort="],
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false
        });

        Assert.Equal(1, Count(path, $"SELECT COUNT(*) FROM urls WHERE url = '{site.Root}ok' AND state = {(int)UrlState.Done}"));
        Assert.Equal((int)SkipReason.Excluded, Count(path, $"SELECT skip_reason FROM urls WHERE url = '{site.Root}trap?sort=price'"));
        Assert.Equal((int)UrlState.Skipped, Count(path, $"SELECT state FROM urls WHERE url = '{site.Root}trap?sort=price'"));
    }

    private static async Task<string> CrawlAsync(TinySite site, CrawlOptions options)
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-js-{Guid.NewGuid():N}.sicrawl");
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        var summary = await job.Completion.WaitAsync(TimeSpan.FromMinutes(3));
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        return path;
    }

    private static long Count(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return Convert.ToInt64(CrawlDatabase.Scalar(connection, sql));
    }

    /// <summary>Two pages: a shell whose script builds a link and an image, and the page that link names.</summary>
    private sealed class TinySite : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        public string Root { get; }

        private TinySite(string root) => Root = root;

        public static async Task<TinySite> StartAsync()
        {
            for (var port = 18_000; port < 18_040; port++)
            {
                var site = new TinySite($"http://127.0.0.1:{port}/");
                try
                {
                    site._listener.Prefixes.Add(site.Root);
                    site._listener.Start();
                    _ = site.ServeAsync(site._stop.Token);
                    return site;
                }
                catch (HttpListenerException)
                {
                    site._listener.Close();
                }
            }

            throw new InvalidOperationException("No free port for the JavaScript site.");
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Close();
            _stop.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? "/";
                var (status, type, body) = path switch
                {
                    "/" => (200, "text/html; charset=utf-8", SHELL),
                    "/full" => (200, "text/html; charset=utf-8", FULL),
                    "/from-js" => (200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>From JS</title></head><body><h1>From JS</h1></body></html>"),
                    "/in-the-html" => (200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>In the HTML</title></head><body><h1>In the HTML</h1></body></html>"),
                    "/rendered-only" => (200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>Rendered product</title></head><body><h1>Rendered product</h1><p>Only the rendered page links here.</p></body></html>"),
                    "/links" => (200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>Links</title></head><body><a href=\"/ok\">Ok</a><a href=\"/trap?sort=price\">Sort</a></body></html>"),
                    "/ok" => (200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>Ok</title></head><body><h1>Ok</h1></body></html>"),
                    "/photo.jpg" => (200, "image/jpeg", "not-a-real-jpeg"),
                    _ => (404, "text/plain", "missing")
                };
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = type;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
                context.Response.Close();
            }
        }
    }
}
