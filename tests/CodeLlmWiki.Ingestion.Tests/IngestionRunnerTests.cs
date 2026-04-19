using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ontology;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class IngestionRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsFailureAndDoesNotExecutePipeline_WhenOntologyIsInvalid()
    {
        var pipeline = new CapturingPipeline();
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);

        var ontologyPath = WriteTempFile(
            """
            version: ""
            predicates: []
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: false);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Failed, result.Status);
        Assert.False(pipeline.WasCalled);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task RunAsync_ReturnsSuccess_WhenOntologyIsValidAndPipelineCompletes()
    {
        var pipeline = new CapturingPipeline();
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);

        var ontologyPath = WriteTempFile(
            """
            version: "1.0.0"
            predicates:
              - id: "core:contains"
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: false);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Succeeded, result.Status);
        Assert.True(pipeline.WasCalled);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeTwo_WhenDiagnosticsExist_AndPartialSuccessNotAllowed()
    {
        var pipeline = new CapturingPipeline(
            diagnostics:
            [
                new IngestionDiagnostic("warn:partial", "diagnostic"),
            ]);
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);
        var ontologyPath = WriteTempFile(
            """
            version: "1.0.0"
            predicates:
              - id: "core:contains"
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: false);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.SucceededWithDiagnostics, result.Status);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeZero_WhenDiagnosticsExist_AndPartialSuccessAllowed()
    {
        var pipeline = new CapturingPipeline(
            diagnostics:
            [
                new IngestionDiagnostic("warn:partial", "diagnostic"),
            ]);
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);
        var ontologyPath = WriteTempFile(
            """
            version: "1.0.0"
            predicates:
              - id: "core:contains"
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: true);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.SucceededWithDiagnostics, result.Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedQualityGate_WhenUnresolvedCallRatioExceedsThreshold()
    {
        var methodId = new EntityId("method:source");
        var unresolvedTargetId = new EntityId("unresolved-call-target:1");
        var callPredicate = CorePredicates.Calls;
        var callTriples = new[]
        {
            new SemanticTriple(new EntityNode(methodId), callPredicate, new EntityNode(unresolvedTargetId)),
            new SemanticTriple(new EntityNode(methodId), callPredicate, new EntityNode(unresolvedTargetId)),
            new SemanticTriple(new EntityNode(methodId), callPredicate, new EntityNode(unresolvedTargetId)),
            new SemanticTriple(new EntityNode(methodId), callPredicate, new EntityNode(unresolvedTargetId)),
        };

        var pipeline = new CapturingPipeline(
            diagnostics:
            [
                new IngestionDiagnostic("method:call:resolution:failed", "failed 1"),
                new IngestionDiagnostic("method:call:internal-target-unmatched", "failed 2"),
            ],
            triples: callTriples);
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);
        var ontologyPath = WriteTempFile(
            """
            version: "1.0.0"
            predicates:
              - id: "core:contains"
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: true);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.FailedQualityGate, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.NotNull(result.QualityGate);
        Assert.False(result.QualityGate!.Passed);
        Assert.Equal(2, result.QualityGate.UnresolvedCallFailures);
        Assert.Equal(4, result.QualityGate.TotalCallResolutionAttempts);
        Assert.Equal(0.5d, result.QualityGate.UnresolvedCallRatio, 8);
    }

    [Fact]
    public async Task RunAsync_ProjectDiscoveryFallbackRemainsWarningLevel_AndNonGating()
    {
        var pipeline = new CapturingPipeline(
            diagnostics:
            [
                new IngestionDiagnostic("project:discovery:fallback", "fallback warning"),
            ]);
        var runner = new IngestionRunner(new OntologyLoader(), pipeline);
        var ontologyPath = WriteTempFile(
            """
            version: "1.0.0"
            predicates:
              - id: "core:contains"
            """);

        var request = new IngestionRunRequest(
            RepositoryPath: ".",
            ConfigPath: null,
            OntologyPath: ontologyPath,
            AllowPartialSuccess: false);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.SucceededWithDiagnostics, result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.QualityGate);
        Assert.True(result.QualityGate!.Passed);
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class CapturingPipeline : IIngestionPipeline
    {
        private readonly IReadOnlyList<IngestionDiagnostic> _diagnostics;
        private readonly IReadOnlyList<SemanticTriple> _triples;

        public CapturingPipeline(
            IReadOnlyList<IngestionDiagnostic>? diagnostics = null,
            IReadOnlyList<SemanticTriple>? triples = null)
        {
            _diagnostics = diagnostics ?? [];
            _triples = triples
                ??
                [
                    new SemanticTriple(
                        new EntityNode(new EntityId("repository:fixture")),
                        new PredicateId("core:contains"),
                        new LiteralNode("noop")),
                ];
        }

        public bool WasCalled { get; private set; }

        public Task<IngestionPipelineResult> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new IngestionPipelineResult(_triples, _diagnostics));
        }
    }
}
