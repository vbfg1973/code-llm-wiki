using CodeLlmWiki.Contracts.Graph;
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

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class CapturingPipeline : IIngestionPipeline
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<SemanticTriple>> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;

            var triple = new SemanticTriple(
                new EntityNode(context.RepositoryId),
                new PredicateId("core:contains"),
                new LiteralNode("noop"));

            return Task.FromResult<IReadOnlyList<SemanticTriple>>(new[] { triple });
        }
    }
}
