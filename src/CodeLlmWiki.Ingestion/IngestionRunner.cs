using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ontology;

namespace CodeLlmWiki.Ingestion;

public sealed class IngestionRunner : IIngestionRunner
{
    private readonly IOntologyLoader _ontologyLoader;
    private readonly IIngestionPipeline _pipeline;
    private readonly IStableIdGenerator _stableIdGenerator;

    public IngestionRunner(
        IOntologyLoader ontologyLoader,
        IIngestionPipeline pipeline,
        IStableIdGenerator? stableIdGenerator = null)
    {
        _ontologyLoader = ontologyLoader;
        _pipeline = pipeline;
        _stableIdGenerator = stableIdGenerator ?? new StableIdGenerator();
    }

    public async Task<IngestionRunResult> RunAsync(IngestionRunRequest request, CancellationToken cancellationToken)
    {
        var ontology = await _ontologyLoader.LoadAsync(request.OntologyPath, cancellationToken);
        if (!ontology.IsValid || ontology.Definition is null)
        {
            var errors = ontology.Issues.Select(x => new IngestionDiagnostic(x.Code, x.Message)).ToArray();
            return new IngestionRunResult(IngestionRunStatus.Failed, 1, errors, []);
        }

        var fullRepositoryPath = Path.GetFullPath(request.RepositoryPath);
        var repositoryId = _stableIdGenerator.Create(new EntityKey("repository", fullRepositoryPath));

        var context = new IngestionExecutionContext(fullRepositoryPath, repositoryId, ontology.Definition);

        var pipelineResult = await _pipeline.ExecuteAsync(context, cancellationToken);
        var diagnostics = pipelineResult.Diagnostics.ToArray();
        var status = diagnostics.Length == 0
            ? IngestionRunStatus.Succeeded
            : IngestionRunStatus.SucceededWithDiagnostics;

        var exitCode = status switch
        {
            IngestionRunStatus.Succeeded => 0,
            IngestionRunStatus.SucceededWithDiagnostics when request.AllowPartialSuccess => 0,
            IngestionRunStatus.SucceededWithDiagnostics => 2,
            _ => 1,
        };

        return new IngestionRunResult(status, exitCode, diagnostics, pipelineResult.Triples);
    }
}
