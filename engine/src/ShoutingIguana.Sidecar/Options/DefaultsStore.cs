using System.Text.Json;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Options;

/// <summary>
/// "My defaults" for new crawls, in <c>defaults.json</c> in the state folder. The start URL, header values and proxy
/// credentials are never written: the UI keeps secrets in the OS credential store, with the user's consent.
/// </summary>
internal sealed class DefaultsStore(SidecarOptions options, ILogger<DefaultsStore> logger)
{
    private const string FILE_NAME = "defaults.json";

    private readonly Lock _sync = new();
    private readonly string _file = Path.Combine(options.StateDirectory, FILE_NAME);

    /// <summary>The user's saved defaults, or the engine's.</summary>
    public CrawlOptionsDto Read()
    {
        lock (_sync)
        {
            if (File.Exists(_file))
            {
                try
                {
                    if (JsonSerializer.Deserialize(File.ReadAllText(_file), SidecarJsonContext.Default.CrawlOptionsDto) is { } saved)
                    {
                        // Whatever the file holds, a secret never leaves it: someone may have edited it by hand.
                        return WithoutSecrets(saved.ToCrawlOptions(out _));
                    }
                }
                catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Could not read {DefaultsFile}; using the engine's defaults", _file);
                }
            }

            return WithoutSecrets(new CrawlOptions { StartUrl = string.Empty });
        }
    }

    /// <summary>Saves the options as the user's defaults, without their secrets, and returns what was saved.</summary>
    /// <exception cref="ProblemException">invalid-options: they wouldn't make a valid crawl, a start URL aside.</exception>
    public CrawlOptionsDto Save(CrawlOptionsDto requested)
    {
        var crawlOptions = requested.ToCrawlOptions(out var mappingErrors);
        var errors = OptionFields.Merge(mappingErrors, crawlOptions.ValidateFields())
            .Where(field => field.Key != OptionFields.START_URL)
            .ToDictionary(StringComparer.Ordinal);
        if (errors.Count > 0)
        {
            throw Problems.InvalidOptions(errors);
        }

        var saved = WithoutSecrets(crawlOptions);
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(options.StateDirectory);
                var temporary = _file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(saved, SidecarJsonContext.Default.CrawlOptionsDto));
                File.Move(temporary, _file, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Problems.FileError("Your defaults couldn't be saved.", exception.Message);
            }
        }

        return saved;
    }

    /// <summary>As a crawl file keeps options (header names without values, the proxy without credentials), and no start URL.</summary>
    private static CrawlOptionsDto WithoutSecrets(CrawlOptions crawlOptions) =>
        CrawlOptionsDto.From(crawlOptions.WithoutSecrets()) with { StartUrl = null };
}
