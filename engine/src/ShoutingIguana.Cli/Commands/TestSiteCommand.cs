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

        var command = new Command("testsite", "Serve a synthetic site with planted SEO issues, for testing and benchmarks.")
        {
            pages, port, externalPort, links, noCompression
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var options = new SyntheticSiteOptions
            {
                PageCount = parse.GetValue(pages),
                ContentLinksPerPage = parse.GetValue(links),
                Compress = !parse.GetValue(noCompression)
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
