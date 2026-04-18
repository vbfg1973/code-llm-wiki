using CodeLlmWiki.Ingestion;

namespace CodeLlmWiki.Cli.Features.Ingest;

public interface IIngestionArtifactPublisher
{
    Task<IngestionArtifactPublishResult> PublishAsync(IngestionArtifactPublishRequest request, CancellationToken cancellationToken);
}
