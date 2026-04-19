namespace CodeLlmWiki.Ingestion;

public interface IIngestionRunner
{
    Task<IngestionRunResult> RunAsync(IngestionRunRequest request, CancellationToken cancellationToken);
}
