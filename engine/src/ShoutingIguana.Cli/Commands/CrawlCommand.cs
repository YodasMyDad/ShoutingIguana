using System.CommandLine;
using System.Globalization;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Cli.Commands;

internal static class CrawlCommand
{
    public static Command Create()
    {
        var url = new Argument<string?>("url") { Description = "Start URL. Omit when resuming.", Arity = ArgumentArity.ZeroOrOne };
        var database = new Option<string?>("--db") { Description = "Crawl file to write (default: <host>-<timestamp>.sicrawl)." };
        var resume = new Option<bool>("--resume") { Description = "Continue the crawl stored in --db." };
        var maxUrls = new Option<int?>("--max-urls") { Description = "Stop after this many internal URLs (0 = no limit)." };
        var maxDepth = new Option<int?>("--max-depth") { Description = "Maximum link depth from the start URL (0 = no limit)." };
        var concurrency = new Option<int?>("--concurrency", "-c") { Description = "Parallel requests to the site (default 5)." };
        var rate = new Option<double?>("--rate") { Description = "Maximum requests per second to the site (default: no cap)." };
        var userAgent = new Option<UserAgentPreset?>("--user-agent") { Description = "ShoutingIguana, ChromeDesktop, ChromeMobile, GooglebotSmartphone, GooglebotDesktop or Bingbot." };
        var userAgentString = new Option<string?>("--user-agent-string") { Description = "Send this exact User-Agent instead of a preset." };
        var headers = new Option<string[]>("--header") { Description = "Extra request header, e.g. \"X-Crawler-Key: secret\". Repeatable." };
        var proxy = new Option<string?>("--proxy") { Description = "http://, https:// or socks5:// proxy URL." };
        var timeout = new Option<int?>("--timeout") { Description = "Per-request timeout in seconds (default 20)." };
        var ignoreRobots = new Option<bool>("--ignore-robots") { Description = "Crawl URLs that robots.txt disallows." };
        var crawlDelay = new Option<bool>("--respect-crawl-delay") { Description = "Honour robots.txt Crawl-delay." };
        var noResources = new Option<bool>("--no-resources") { Description = "Skip status checks of images, CSS and JavaScript." };
        var noExternal = new Option<bool>("--no-external") { Description = "Skip status checks of links to other sites." };
        var noSitemaps = new Option<bool>("--no-sitemaps") { Description = "Do not read XML sitemaps." };
        var subdomains = new Option<bool>("--subdomains") { Description = "Treat every subdomain as part of the site." };
        var anyFolder = new Option<bool>("--any-folder") { Description = "Crawl outside the start URL's folder." };
        var followNofollow = new Option<bool>("--follow-nofollow") { Description = "Follow nofollow links." };
        var storeHtml = new Option<bool>("--store-html") { Description = "Keep compressed HTML of every page." };
        var noAnalyze = new Option<bool>("--no-analyze") { Description = "Skip the SEO checks when the crawl completes." };
        var quiet = new Option<bool>("--quiet", "-q") { Description = "No progress line." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Detailed engine logging." };

        var command = new Command("crawl", "Crawl a site into a .sicrawl file.")
        {
            url, database, resume, maxUrls, maxDepth, concurrency, rate, userAgent, userAgentString, headers, proxy, timeout,
            ignoreRobots, crawlDelay, noResources, noExternal, noSitemaps, subdomains, anyFolder, followNofollow, storeHtml,
            noAnalyze, quiet, verbose
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var databasePath = parse.GetValue(database);
            var isResume = parse.GetValue(resume);
            CrawlOptions options;

            if (isResume)
            {
                if (databasePath is null || !File.Exists(databasePath))
                {
                    return Fail("--resume needs --db pointing at an existing crawl file.");
                }

                var stored = CrawlDatabase.ReadOptions(databasePath);
                options = stored with
                {
                    Concurrency = parse.GetValue(concurrency) ?? stored.Concurrency,
                    MaxRequestsPerSecond = parse.GetValue(rate) ?? stored.MaxRequestsPerSecond
                };
            }
            else
            {
                var startUrl = parse.GetValue(url);
                if (startUrl is null)
                {
                    return Fail("Give a start URL, or --resume with --db.");
                }

                if (!TryParseHeaders(parse.GetValue(headers) ?? [], out var extraHeaders, out var headerError))
                {
                    return Fail(headerError);
                }

                var defaults = new CrawlOptions { StartUrl = startUrl };
                var customAgent = parse.GetValue(userAgentString);
                options = defaults with
                {
                    MaxUrls = parse.GetValue(maxUrls) ?? defaults.MaxUrls,
                    MaxDepth = parse.GetValue(maxDepth) ?? defaults.MaxDepth,
                    Concurrency = parse.GetValue(concurrency) ?? defaults.Concurrency,
                    MaxRequestsPerSecond = parse.GetValue(rate) ?? defaults.MaxRequestsPerSecond,
                    UserAgent = customAgent is not null ? UserAgentPreset.Custom : parse.GetValue(userAgent) ?? defaults.UserAgent,
                    CustomUserAgent = customAgent,
                    ExtraHeaders = extraHeaders,
                    ProxyUrl = parse.GetValue(proxy),
                    RequestTimeoutSeconds = parse.GetValue(timeout) ?? defaults.RequestTimeoutSeconds,
                    RespectRobotsTxt = !parse.GetValue(ignoreRobots),
                    RespectCrawlDelay = parse.GetValue(crawlDelay),
                    CheckResources = !parse.GetValue(noResources),
                    CheckExternalLinks = !parse.GetValue(noExternal),
                    DiscoverSitemaps = !parse.GetValue(noSitemaps),
                    IncludeSubdomains = parse.GetValue(subdomains),
                    StayInStartFolder = !parse.GetValue(anyFolder),
                    FollowNofollow = parse.GetValue(followNofollow),
                    StoreHtml = parse.GetValue(storeHtml)
                };
                databasePath ??= DefaultDatabasePath(startUrl);
            }

            var errors = options.Validate();
            if (errors.Count > 0)
            {
                return Fail(string.Join(Environment.NewLine, errors));
            }

            using var loggerFactory = Logging.Create(parse.GetValue(verbose));
            var observer = new ConsoleCrawlObserver(parse.GetValue(quiet));
            Console.WriteLine($"{(isResume ? "Resuming" : "Crawling")} {options.StartUrl} -> {Path.GetFullPath(databasePath)}");

            await using var job = new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, databasePath, isResume), observer);
            await using var stopOnCancel = cancellationToken.Register(() => job.StopAsync());
            var summary = await job.Completion;
            observer.EndProgressLine();

            Console.WriteLine(summary.Status switch
            {
                CrawlStatus.Completed => $"Done: {summary.Progress.Fetched:N0} URLs fetched ({summary.Progress.HtmlPages:N0} pages) in {Format.Duration(summary.Progress.Elapsed)}.",
                CrawlStatus.Stopped => $"Stopped. Continue with: si crawl --resume --db \"{databasePath}\"  (or check what's there: si analyze \"{databasePath}\")",
                _ => $"Failed: {summary.Error}"
            });

            if (summary.Status == CrawlStatus.Completed && !parse.GetValue(noAnalyze))
            {
                Console.WriteLine();
                await AnalyzeCommand.RunAsync(databasePath, loggerFactory, cancellationToken);
            }

            return summary.Status switch
            {
                CrawlStatus.Completed => 0,
                CrawlStatus.Stopped => 130,
                _ => 1
            };
        });

        return command;
    }

    private static bool TryParseHeaders(string[] values, out Dictionary<string, string> headers, out string error)
    {
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        foreach (var value in values)
        {
            var colon = value.IndexOf(':');
            if (colon <= 0)
            {
                error = $"Header '{value}' must look like 'Name: value'.";
                return false;
            }

            headers[value[..colon].Trim()] = value[(colon + 1)..].Trim();
        }

        return true;
    }

    private static string DefaultDatabasePath(string startUrl)
    {
        var host = UrlNormalizer.TryNormalize(startUrl, out var normalized) ? new Uri(normalized).Host : "crawl";
        return $"{host}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.sicrawl";
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
