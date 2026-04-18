namespace CodeLlmWiki.Ingestion;

public interface IIngestionPipeline
{
    Task<IngestionPipelineResult> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken);
}
