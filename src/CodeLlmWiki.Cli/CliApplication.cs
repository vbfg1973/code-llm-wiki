using System.Globalization;
using System.Text.Json;
using CodeLlmWiki.Cli.Commands;
using CodeLlmWiki.Cli.Config;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Ingestion;
using CommandLine;

namespace CodeLlmWiki.Cli;

public sealed class CliApplication
{
    private const string DefaultOntologyPath = "ontology/ontology.v1.yaml";
    private const string DefaultOutputRoot = "artifacts";

    private readonly IIngestionRunner _runner;
    private readonly IIngestionArtifactPublisher _artifactPublisher;

    public CliApplication(
        IIngestionRunner runner,
        IIngestionArtifactPublisher? artifactPublisher = null)
    {
        _runner = runner;
        _artifactPublisher = artifactPublisher ?? NoOpIngestionArtifactPublisher.Instance;
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

        var outputRoot = options.OutputRoot
            ?? config.OutputRoot
            ?? DefaultOutputRoot;

        var maxMergeEntriesPerFile = options.MaxMergeEntriesPerFile
            ?? config.MaxMergeEntriesPerFile;
        var metricComputationMdop = options.MetricComputationMaxDegreeOfParallelism
            ?? config.MetricComputationMaxDegreeOfParallelism;

        var request = new IngestionRunRequest(
            RepositoryPath: options.RepositoryPath,
            ConfigPath: options.ConfigPath,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: allowPartial);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var result = await _runner.RunAsync(request, cancellationToken);
        var completedAtUtc = DateTimeOffset.UtcNow;

        var publication = await _artifactPublisher.PublishAsync(
            new IngestionArtifactPublishRequest(
                RepositoryPath: options.RepositoryPath,
                OutputRootPath: outputRoot,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                RunResult: result,
                MaxMergeEntriesPerFile: maxMergeEntriesPerFile,
                MetricComputationMaxDegreeOfParallelism: metricComputationMdop),
            cancellationToken);

        if (!publication.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(publication.FailureReason))
            {
                Console.Error.WriteLine($"Artifact publication failed: {publication.FailureReason}");
            }

            return 1;
        }

        if (result.Status == IngestionRunStatus.FailedQualityGate && result.QualityGate is { } qualityGate)
        {
            Console.Error.WriteLine(
                "Quality gate failed: unresolved-call-ratio "
                + $"{qualityGate.UnresolvedCallRatio.ToString("0.####", CultureInfo.InvariantCulture)} "
                + "exceeds threshold "
                + $"{qualityGate.Threshold.ToString("0.####", CultureInfo.InvariantCulture)} "
                + $"(unresolved={qualityGate.UnresolvedCallFailures}, total={qualityGate.TotalCallResolutionAttempts}).");
        }

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
