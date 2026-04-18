using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Ingestion;

public interface IIngestionPipeline
{
    Task<IReadOnlyList<SemanticTriple>> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken);
}
