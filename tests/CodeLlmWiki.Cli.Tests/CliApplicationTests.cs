using CodeLlmWiki.Cli;
using CodeLlmWiki.Ingestion;

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
                []),
        };

        var app = new CliApplication(runner);

        var first = await app.RunAsync(["ingest", "--path", "."], CancellationToken.None);
        var second = await app.RunAsync(["ingest", "--path", "."], CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(2, second);
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
            []);

        public Task<IngestionRunResult> RunAsync(IngestionRunRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }
}
