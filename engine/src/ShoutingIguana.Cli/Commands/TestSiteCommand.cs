using System.CommandLine;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Cli.Commands;

internal static class TestSiteCommand
{
    /// <summary>Printed once the site is listening; the benchmark waits for this line.</summary>
    public const string READY_PREFIX = "READY ";

    public static Command Create()
    {
        var pages = new Option<int>("--pages") { Description = "Number of pages (home + products).", DefaultValueFactory = _ => 10_000 };
        var port = new Option<int>("--port") { Description = "Port for the site (0 = pick a free one)." };
        var externalPort = new Option<int>("--external-port") { Description = "Port for the 'external' site (0 = pick a free one)." };
        var links = new Option<int>("--links") { Description = "Content links per product page.", DefaultValueFactory = _ => 20 };
        var noCompression = new Option<bool>("--no-compression") { Description = "Serve uncompressed responses." };
        var deadTwin = new Option<bool>("--dead-twin") { Description = "Link the site's https:// version, which doesn't answer, so some of its URLs aren't checked." };
        var hidden = new Option<bool>("--hidden-from-search") { Description = "Make the home page and every product page noindex, so the site as a whole is hidden from search." };
        var rateLimit = new Option<int>("--rate-limit")
        {
            Description = "Answer product pages 429 Too Many Requests once more than this many are asked for in a second, with a penalty that further requests renew (0 = no limit)."
        };
        var penalty = new Option<int>("--rate-limit-penalty") { Description = "The rate limit's penalty and Retry-After, in seconds.", DefaultValueFactory = _ => 5 };
        var neverRelents = new Option<bool>("--rate-limit-never-relents") { Description = "Answer every product page 429 Too Many Requests." };

        var command = new Command("testsite", "Serve a synthetic site with planted SEO issues, for testing and benchmarks.")
        {
            pages, port, externalPort, links, noCompression, deadTwin, hidden, rateLimit, penalty, neverRelents
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var options = new SyntheticSiteOptions
            {
                PageCount = parse.GetValue(pages),
                ContentLinksPerPage = parse.GetValue(links),
                Compress = !parse.GetValue(noCompression),
                DeadTwin = parse.GetValue(deadTwin),
                HiddenFromSearch = parse.GetValue(hidden),
                RateLimit = parse.GetValue(rateLimit) > 0 || parse.GetValue(neverRelents)
                    ? new SyntheticRateLimit(Math.Max(1, parse.GetValue(rateLimit)), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(parse.GetValue(penalty)),
                        parse.GetValue(neverRelents))
                    : null
            };

            await using var server = await SyntheticSiteServer.StartAsync(options, parse.GetValue(port), parse.GetValue(externalPort), cancellationToken);
            Console.WriteLine($"{READY_PREFIX}{server.BaseUri} {server.ExternalBaseUri}");
            Console.Out.Flush();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C or the parent process closing: shut down cleanly.
            }

            return 0;
        });

        return command;
    }
}
