using System.CommandLine;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Cli.Commands;

internal static class CrawlCommand
{
    /// <summary>Exit code for a crawl that stopped itself, resumable, because the site stopped answering.</summary>
    public const int STOPPED_ITSELF = 3;

    public static Command Create()
    {
        var url = new Argument<string?>("url") { Description = "Start URL. Omit when resuming.", Arity = ArgumentArity.ZeroOrOne };
        var database = new Option<string?>("--db") { Description = "Crawl file to write (default: <host>-<timestamp>.sicrawl)." };
        var resume = new Option<bool>("--resume") { Description = "Continue the crawl stored in --db." };
        var maxUrls = new Option<int?>("--max-urls") { Description = "Stop after this many internal URLs (0 = no limit)." };
        var maxDepth = new Option<int?>("--max-depth") { Description = "Maximum link depth from the start URL (0 = no limit)." };
        var concurrency = new Option<int?>("--concurrency", "-c") { Description = "Most parallel requests to the site (default 5). Each host starts at one and speeds up while it answers well." };
        var rate = new Option<double?>("--rate") { Description = "Most requests per second to the site (default: no cap). Each host starts at one a second, and slows down when it asks." };
        var userAgent = new Option<UserAgentPreset?>("--user-agent") { Description = "ShoutingIguana, ChromeDesktop, ChromeMobile, GooglebotSmartphone, GooglebotDesktop or Bingbot." };
        var userAgentString = new Option<string?>("--user-agent-string") { Description = "Send this exact User-Agent instead of a preset." };
        var headers = new Option<string[]>("--header") { Description = "Extra request header, e.g. \"X-Crawler-Key: secret\". Repeatable." };
        var proxy = new Option<string?>("--proxy") { Description = "http://, https:// or socks5:// proxy URL." };
        var signatureFiles = SignatureFile.CreateOption();
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
        var render = new Option<JavaScriptRendering?>("--render")
        {
            Description = "JavaScript rendering: Automatic (a client-rendered shell), JavaScript (every HTML page, and links from the raw HTML), Off, or Matching (only --render-url)."
        };
        var renderUrls = new Option<string[]>("--render-url") { Description = "Regular expression matched against the whole URL. With --render Matching, only these URLs are rendered. Repeatable." };
        var include = new Option<string[]>("--include") { Description = "Regular expression matched against the whole URL. When any are set, only matching URLs are fetched. Repeatable." };
        var exclude = new Option<string[]>("--exclude") { Description = "Regular expression matched against the whole URL. A match is recorded and not fetched. Repeatable." };
        var extract = new Option<string[]>("--extract") { Description = "Custom extraction rule: Name=css:selector, Name=xpath:expression or Name=regex:pattern. Repeatable." };
        var noAnalyze = new Option<bool>("--no-analyze") { Description = "Skip the SEO checks when the crawl completes." };
        var quiet = new Option<bool>("--quiet", "-q") { Description = "No progress line." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Detailed engine logging." };

        var command = new Command("crawl", "Crawl a site into a .sicrawl file.")
        {
            url, database, resume, maxUrls, maxDepth, concurrency, rate, userAgent, userAgentString, headers, proxy, signatureFiles, timeout,
            ignoreRobots, crawlDelay, noResources, noExternal, noSitemaps, subdomains, anyFolder, followNofollow, storeHtml,
            render, renderUrls, include, exclude, extract,
            noAnalyze, quiet, verbose
        };

        // A resumed crawl keeps the scope and rules it started with; only how it talks to the site can change.
        Option[] fixedOnResume = [maxUrls, maxDepth, userAgent, userAgentString, ignoreRobots, crawlDelay, noResources, noExternal,
            noSitemaps, subdomains, anyFolder, followNofollow, storeHtml, render, renderUrls, include, exclude, extract];

        command.SetAction((parse, cancellationToken) => CommandErrors.RunAsync(async () =>
        {
            var databasePath = parse.GetValue(database);
            var isResume = parse.GetValue(resume);
            if (!TryParseHeaders(parse.GetValue(headers) ?? [], out var extraHeaders, out var headerError))
            {
                return Fail(headerError);
            }

            if (!TryParseExtractions(parse.GetValue(extract) ?? [], out var extractionRules, out var extractError))
            {
                return Fail(extractError);
            }

            CrawlOptions options;
            if (isResume)
            {
                if (databasePath is null || !File.Exists(databasePath))
                {
                    return Fail("--resume needs --db pointing at an existing crawl file.");
                }

                List<string> unchangeable = [.. fixedOnResume.Where(option => parse.GetResult(option) is { Implicit: false }).Select(option => option.Name)];
                if (parse.GetValue(url) is not null)
                {
                    unchangeable.Insert(0, "the start URL");
                }

                if (unchangeable.Count > 0)
                {
                    return Fail($"{string.Join(", ", unchangeable)} can't change when resuming: a crawl keeps the settings it started with. " +
                                "When resuming you can change --concurrency, --rate, --timeout, --header, --proxy and --signature-file.");
                }

                var plan = CrawlDatabase.ReadOptions(databasePath).PlanResume(new ResumeChanges
                {
                    Concurrency = parse.GetValue(concurrency),
                    MaxRequestsPerSecond = parse.GetValue(rate),
                    RequestTimeoutSeconds = parse.GetValue(timeout),
                    ExtraHeaders = extraHeaders,
                    ProxyUrl = parse.GetValue(proxy)
                });

                if (plan.MissingProxyCredentials)
                {
                    return Fail("This crawl used a proxy with a user name and password, which crawl files don't store. Pass --proxy again.");
                }

                // Header values aren't stored either: warn about any that weren't given again, rather than send them empty.
                var notResent = plan.MissingHeaders;
                if (notResent.Count > 0)
                {
                    Console.Error.WriteLine($"Warning: this crawl was started with the header{(notResent.Count == 1 ? string.Empty : "s")} " +
                                            $"{string.Join(", ", notResent)}, which crawl files don't store. Pass {(notResent.Count == 1 ? "it" : "them")} " +
                                            "again with --header to keep sending them.");
                }

                options = plan.Options;
            }
            else
            {
                var startUrl = parse.GetValue(url);
                if (startUrl is null)
                {
                    return Fail("Give a start URL, or --resume with --db.");
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
                    StoreHtml = parse.GetValue(storeHtml),
                    Rendering = parse.GetValue(render) ?? defaults.Rendering,
                    RenderPatterns = parse.GetValue(renderUrls) ?? [],
                    IncludePatterns = parse.GetValue(include) ?? [],
                    ExcludePatterns = parse.GetValue(exclude) ?? [],
                    ExtractionRules = extractionRules
                };
                databasePath ??= CrawlDatabase.DefaultDatabasePath(startUrl);
            }

            // A crawl file keeps no signature, so a resumed crawl sends one only when it's given again.
            if (!SignatureFile.TryRead(parse.GetValue(signatureFiles) ?? [], options.StartUrl, out var signatures, out var signatureError))
            {
                return Fail(signatureError);
            }

            options = options with { Signatures = signatures };
            var errors = options.Validate();
            if (errors.Count > 0)
            {
                return Fail(string.Join(Environment.NewLine, errors));
            }

            using var loggerFactory = Logging.Create(parse.GetValue(verbose));
            var observer = new ConsoleCrawlObserver(parse.GetValue(quiet));
            Console.WriteLine($"{(isResume ? "Resuming" : "Crawling")} {options.StartUrl} -> {Path.GetFullPath(databasePath)}");
            SignatureFile.SayWhatIsSent(signatures);

            await using var job = new CrawlEngine(loggerFactory).Start(new CrawlRequest(options, databasePath, isResume), observer);
            await using var stopOnCancel = cancellationToken.Register(() => job.StopAsync());
            var summary = await job.Completion;
            observer.EndProgressLine();

            Console.WriteLine(summary.Status switch
            {
                CrawlStatus.Completed => $"Done: {Format.Count(summary.Progress.Fetched, "URL", "URLs")} fetched ({Format.Count(summary.Progress.HtmlPages, "page", "pages")}) in {Format.Duration(summary.Progress.Elapsed)}.",
                CrawlStatus.Stopped => $"Stopped. Continue with: si crawl --resume --db \"{databasePath}\"  (or check what's there: si analyze \"{databasePath}\")",
                _ => $"Failed: {summary.Error}"
            });

            if (summary.Status == CrawlStatus.Completed && !parse.GetValue(noAnalyze))
            {
                Console.WriteLine();
                return await AnalyzeCommand.RunAsync(databasePath, loggerFactory, cancellationToken);
            }

            // Stopped without Ctrl+C means the crawl stopped itself (the site stopped answering): resume it later.
            return summary.Status switch
            {
                CrawlStatus.Completed => 0,
                CrawlStatus.Stopped => cancellationToken.IsCancellationRequested ? AnalyzeCommand.CANCELLED : STOPPED_ITSELF,
                _ => 1
            };
        }));

        return command;
    }

    internal static bool TryParseHeaders(string[] values, out Dictionary<string, string> headers, out string error)
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

    internal static bool TryParseExtractions(string[] values, out List<ExtractionRule> rules, out string error)
    {
        rules = [];
        error = string.Empty;
        foreach (var value in values)
        {
            if (!ExtractionRule.TryParse(value, out var rule, out error))
            {
                return false;
            }

            rules.Add(rule);
        }

        return true;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
