using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.Quality;
using CodeLlmWiki.Ontology;

namespace CodeLlmWiki.Ingestion;

public sealed class IngestionRunner : IIngestionRunner
{
    private readonly IOntologyLoader _ontologyLoader;
    private readonly IIngestionPipeline _pipeline;
    private readonly IStableIdGenerator _stableIdGenerator;
    private readonly IUnresolvedCallRatioQualityGateEvaluator _qualityGateEvaluator;

    public IngestionRunner(
        IOntologyLoader ontologyLoader,
        IIngestionPipeline pipeline,
        IStableIdGenerator? stableIdGenerator = null,
        IUnresolvedCallRatioQualityGateEvaluator? qualityGateEvaluator = null)
    {
        _ontologyLoader = ontologyLoader;
        _pipeline = pipeline;
        _stableIdGenerator = stableIdGenerator ?? new StableIdGenerator();
        _qualityGateEvaluator = qualityGateEvaluator ?? new UnresolvedCallRatioQualityGateEvaluator();
    }

    public async Task<IngestionRunResult> RunAsync(IngestionRunRequest request, CancellationToken cancellationToken)
    {
        var ontology = await _ontologyLoader.LoadAsync(request.OntologyPath, cancellationToken);
        if (!ontology.IsValid || ontology.Definition is null)
        {
            var errors = ontology.Issues.Select(x => new IngestionDiagnostic(x.Code, x.Message)).ToArray();
            return new IngestionRunResult(IngestionRunStatus.Failed, 1, errors, default, []);
        }

        var fullRepositoryPath = Path.GetFullPath(request.RepositoryPath);
        var repositoryId = _stableIdGenerator.Create(new EntityKey("repository", fullRepositoryPath));

        var context = new IngestionExecutionContext(
            RepositoryPath: fullRepositoryPath,
            RepositoryId: repositoryId,
            Ontology: ontology.Definition,
            SemanticCallGraphMaxDegreeOfParallelism: request.SemanticCallGraphMaxDegreeOfParallelism);

        var pipelineResult = await _pipeline.ExecuteAsync(context, cancellationToken);
        var diagnostics = pipelineResult.Diagnostics.ToArray();
        var baseStatus = diagnostics.Length == 0
            ? IngestionRunStatus.Succeeded
            : IngestionRunStatus.SucceededWithDiagnostics;

        var baseExitCode = baseStatus switch
        {
            IngestionRunStatus.Succeeded => 0,
            IngestionRunStatus.SucceededWithDiagnostics when request.AllowPartialSuccess => 0,
            IngestionRunStatus.SucceededWithDiagnostics => 2,
            _ => 1,
        };

        var qualityGate = _qualityGateEvaluator.Evaluate(
            new UnresolvedCallRatioQualityGateRequest(
                Diagnostics: diagnostics,
                TotalCallResolutionAttempts: CountCallResolutionAttempts(pipelineResult.Triples)));
        if (qualityGate.Passed)
        {
            return new IngestionRunResult(baseStatus, baseExitCode, diagnostics, repositoryId, pipelineResult.Triples, qualityGate.Evidence);
        }

        return new IngestionRunResult(
            Status: IngestionRunStatus.FailedQualityGate,
            ExitCode: 3,
            Diagnostics: diagnostics,
            RepositoryId: repositoryId,
            Triples: pipelineResult.Triples,
            QualityGate: qualityGate.Evidence);
    }

    private static int CountCallResolutionAttempts(IReadOnlyList<SemanticTriple> triples)
    {
        return triples.Count(x => x.Predicate == CorePredicates.Calls);
    }
}
