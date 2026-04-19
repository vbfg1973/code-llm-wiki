namespace CodeLlmWiki.Ingestion;

public sealed class NoOpIngestionPipeline : IIngestionPipeline
{
    public Task<IngestionPipelineResult> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new IngestionPipelineResult([], []));
    }
}
