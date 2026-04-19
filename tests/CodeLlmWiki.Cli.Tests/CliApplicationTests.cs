using CodeLlmWiki.Cli;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ingestion.Quality;

namespace CodeLlmWiki.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_AppliesCommandLineOverrideOverConfigValue()
    {
        var configPath = WriteTempFile(
            """
            {
              "ontologyPath": "./ontology/ontology.v1.yaml",
              "allowPartialSuccess": false
            }
            """);

        var runner = new CapturingRunner();
        var app = new CliApplication(runner);

        var exitCode = await app.RunAsync(
            [
                "ingest",
                "--path", ".",
                "--config", configPath,
                "--allow-partial-success", "true",
            ],
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(runner.LastRequest);
        Assert.True(runner.LastRequest!.AllowPartialSuccess);
    }

    [Fact]
    public async Task RunAsync_ReturnsRunnerExitCodeDeterministically()
    {
        var runner = new CapturingRunner
        {
            NextResult = new IngestionRunResult(
                IngestionRunStatus.SucceededWithDiagnostics,
                2,
                [new IngestionDiagnostic("warn:0001", "warning")],
                new StableIdGenerator().Create(new EntityKey("repository", ".")),
                []),
        };

        var app = new CliApplication(runner);

        var first = await app.RunAsync(["ingest", "--path", "."], CancellationToken.None);
        var second = await app.RunAsync(["ingest", "--path", "."], CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task RunAsync_PassesOutputRootOptionOverrideToPublisher()
    {
        var configPath = WriteTempFile(
            """
            {
              "ontologyPath": "./ontology/ontology.v1.yaml",
              "outputRoot": "./from-config"
            }
            """);

        var runner = new CapturingRunner();
        var publisher = new CapturingPublisher();
        var app = new CliApplication(runner, publisher);

        var exitCode = await app.RunAsync(
            [
                "ingest",
                "--path", ".",
                "--config", configPath,
                "--output-root", "./from-option",
            ],
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(publisher.LastRequest);
        Assert.Equal("./from-option", publisher.LastRequest!.OutputRootPath);
    }

    [Fact]
    public async Task RunAsync_PassesMaxMergeEntriesPerFileFromConfigAndOption()
    {
        var configPath = WriteTempFile(
            """
            {
              "ontologyPath": "./ontology/ontology.v1.yaml",
              "max_merge_entries_per_file": 7,
              "metric_computation_max_degree_of_parallelism": 5
            }
            """);

        var runner = new CapturingRunner();
        var publisher = new CapturingPublisher();
        var app = new CliApplication(runner, publisher);

        var exitCode = await app.RunAsync(
            [
                "ingest",
                "--path", ".",
                "--config", configPath,
                "--max-merge-entries-per-file", "3",
                "--metric-mdop", "4",
            ],
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(publisher.LastRequest);
        Assert.Equal(3, publisher.LastRequest!.MaxMergeEntriesPerFile);
        Assert.Equal(4, publisher.LastRequest.MetricComputationMaxDegreeOfParallelism);
    }

    [Fact]
    public async Task RunAsync_WritesQualityGateFailureDetailsToStderr()
    {
        var runner = new CapturingRunner
        {
            NextResult = new IngestionRunResult(
                Status: IngestionRunStatus.FailedQualityGate,
                ExitCode: 3,
                Diagnostics: [new IngestionDiagnostic("method:call:resolution:failed", "failed")],
                RepositoryId: new StableIdGenerator().Create(new EntityKey("repository", ".")),
                Triples: [],
                QualityGate: new UnresolvedCallRatioQualityGateEvidence(
                    GateId: "quality:unresolved-call-ratio",
                    UnresolvedCallFailures: 3,
                    TotalCallResolutionAttempts: 4,
                    UnresolvedCallRatio: 0.75d,
                    Threshold: 0.25d,
                    Passed: false)),
        };
        var publisher = new CapturingPublisher();
        var app = new CliApplication(runner, publisher);

        var originalError = Console.Error;
        using var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        try
        {
            var exitCode = await app.RunAsync(["ingest", "--path", "."], CancellationToken.None);

            Assert.Equal(3, exitCode);
            var stderr = errorWriter.ToString();
            Assert.Contains("Quality gate failed", stderr, StringComparison.Ordinal);
            Assert.Contains("0.75", stderr, StringComparison.Ordinal);
            Assert.Contains("0.25", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class CapturingRunner : IIngestionRunner
    {
        public IngestionRunRequest? LastRequest { get; private set; }

        public IngestionRunResult NextResult { get; set; } = new(
            IngestionRunStatus.Succeeded,
            0,
            [],
            new StableIdGenerator().Create(new EntityKey("repository", ".")),
            []);

        public Task<IngestionRunResult> RunAsync(IngestionRunRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class CapturingPublisher : IIngestionArtifactPublisher
    {
        public IngestionArtifactPublishRequest? LastRequest { get; private set; }

        public Task<IngestionArtifactPublishResult> PublishAsync(IngestionArtifactPublishRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new IngestionArtifactPublishResult(
                Succeeded: true,
                LatestPromoted: false,
                RunId: "run",
                RunDirectory: "run",
                ManifestPath: "manifest",
                WikiDirectory: null,
                GraphMlPath: null,
                FailureReason: null));
        }
    }
}
