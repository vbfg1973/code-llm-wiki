using System.Text.Json;
using CodeLlmWiki.Cli.Commands;
using CodeLlmWiki.Cli.Config;
using CodeLlmWiki.Ingestion;
using CommandLine;

namespace CodeLlmWiki.Cli;

public sealed class CliApplication
{
    private const string DefaultOntologyPath = "ontology/ontology.v1.yaml";
    private readonly IIngestionRunner _runner;

    public CliApplication(IIngestionRunner runner)
    {
        _runner = runner;
    }

    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var parser = new Parser(settings =>
        {
            settings.HelpWriter = Console.Out;
            settings.CaseInsensitiveEnumValues = true;
        });

        return parser.ParseArguments<IngestOptions>(args)
            .MapResult(
                ingest => RunIngestAsync(ingest, cancellationToken),
                _ => Task.FromResult(1));
    }

    private async Task<int> RunIngestAsync(IngestOptions options, CancellationToken cancellationToken)
    {
        var config = await LoadConfigAsync(options.ConfigPath, cancellationToken);

        var allowPartial = ParseNullableBool(options.AllowPartialSuccess)
            ?? config.AllowPartialSuccess
            ?? false;

        var ontologyPath = options.OntologyPath
            ?? config.OntologyPath
            ?? DefaultOntologyPath;

        var request = new IngestionRunRequest(
            RepositoryPath: options.RepositoryPath,
            ConfigPath: options.ConfigPath,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: allowPartial);

        var result = await _runner.RunAsync(request, cancellationToken);

        return result.ExitCode;
    }

    private static async Task<IngestionCliConfig> LoadConfigAsync(string? configPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return IngestionCliConfig.Empty;
        }

        await using var stream = File.OpenRead(configPath);

        var config = await JsonSerializer.DeserializeAsync<IngestionCliConfig>(stream, cancellationToken: cancellationToken);

        return config ?? IngestionCliConfig.Empty;
    }

    private static bool? ParseNullableBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : null;
    }
}
