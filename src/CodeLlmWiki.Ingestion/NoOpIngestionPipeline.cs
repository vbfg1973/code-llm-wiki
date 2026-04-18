using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Ingestion;

public sealed class NoOpIngestionPipeline : IIngestionPipeline
{
    public Task<IReadOnlyList<SemanticTriple>> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SemanticTriple>>([]);
    }
}
