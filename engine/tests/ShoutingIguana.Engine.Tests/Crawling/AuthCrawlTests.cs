using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A page behind a sign-in is read when the crawl is given that sign-in, and the password is not kept in the crawl
/// file. HTTP Basic, HTTP Digest, a cookie sent from the start, and a form filled in the browser.
/// </summary>
public sealed class AuthCrawlTests : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2, Sitemaps = false });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BasicAuthReadsThePageAndDoesNotStoreThePassword()
    {
        var path = NewDatabase();
        var start = Url(SyntheticSite.HTTP_AUTH_PATH);
        var summary = await RunAsync(path, start, options => options with
        {
            HttpLogins = [new SiteLogin { Username = SyntheticSite.AUTH_USER, Password = SyntheticSite.AUTH_PASSWORD }]
        });

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.DoesNotContain(SyntheticSite.AUTH_PASSWORD, File.ReadAllText(path));
        Assert.Equal("", CrawlDatabase.ReadOptions(path).HttpLogins[0].Password);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path)[IssueCatalog.TITLE_MISSING.Id]);
    }

    [Fact]
    public async Task AWrongPasswordStaysSignedOut()
    {
        var path = NewDatabase();
        var start = Url(SyntheticSite.HTTP_AUTH_PATH);
        var summary = await RunAsync(path, start, options => options with
        {
            HttpLogins = [new SiteLogin { Username = SyntheticSite.AUTH_USER, Password = "wrong" }]
        });

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(((int)FetchOutcome.ClientError).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("401", Scalar(path, start, "r.status_code"));
    }

    [Fact]
    public async Task DigestAuthReadsThePage()
    {
        var path = NewDatabase();
        var start = Url(SyntheticSite.DIGEST_AUTH_PATH);
        var summary = await RunAsync(path, start, options => options with
        {
            HttpLogins = [new SiteLogin { Username = SyntheticSite.AUTH_USER, Password = SyntheticSite.AUTH_PASSWORD }]
        });

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
    }

    [Fact]
    public async Task ACookieSentFromTheStartOpensTheGate()
    {
        var path = NewDatabase();
        var start = Url(SyntheticSite.COOKIE_GATE_PATH);
        var locked = await RunAsync(path, start, options => options);
        Assert.Equal(CrawlStatus.Completed, locked.Status);
        Assert.Equal("403", Scalar(path, start, "r.status_code"));

        var opened = NewDatabase();
        var summary = await RunAsync(opened, start, options => options with
        {
            Cookies = [new SentCookie { Name = "session", Value = "planted" }]
        });
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal("200", Scalar(opened, start, "r.status_code"));
        Assert.Equal("", CrawlDatabase.ReadOptions(opened).Cookies[0].Value);
        Assert.DoesNotContain("planted", File.ReadAllText(opened));
    }

    [Fact]
    public async Task FormLoginSetsTheCookieAndAWrongPasswordFailsTheCrawl()
    {
        var path = NewDatabase();
        var start = Url(SyntheticSite.FORM_MEMBER_PATH);
        var summary = await RunAsync(path, start, options => options with
        {
            FormLogin = new FormLogin
            {
                Url = Url(SyntheticSite.FORM_LOGIN_PATH),
                Username = SyntheticSite.AUTH_USER,
                Password = SyntheticSite.AUTH_PASSWORD
            }
        });

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.DoesNotContain(SyntheticSite.AUTH_PASSWORD, File.ReadAllText(path));

        var refused = NewDatabase();
        var failed = await RunAsync(refused, start, options => options with
        {
            FormLogin = new FormLogin
            {
                Url = Url(SyntheticSite.FORM_LOGIN_PATH),
                Username = SyntheticSite.AUTH_USER,
                Password = "wrong"
            }
        });
        Assert.Equal(CrawlStatus.Failed, failed.Status);
        Assert.Contains("Sign-in didn't get past", failed.Error);
    }

    private string Url(string path) => new Uri(_server.BaseUri, path).AbsoluteUri;

    private async Task<CrawlSummary> RunAsync(string path, string start, Func<CrawlOptions, CrawlOptions> edit)
    {
        var options = edit(new CrawlOptions
        {
            StartUrl = start,
            Concurrency = 2,
            Rendering = JavaScriptRendering.Off,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false
        });
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        return await job.Completion.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static string Scalar(string path, string url, string column) =>
        Assert.Single(TestCrawls.Rows(path, $"SELECT {column} FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url = '{url}'"));

    private string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-auth-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }
}
