namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed class NoOpIngestionArtifactPublisher : IIngestionArtifactPublisher
{
    public static readonly NoOpIngestionArtifactPublisher Instance = new();

    private NoOpIngestionArtifactPublisher()
    {
    }

    public Task<IngestionArtifactPublishResult> PublishAsync(IngestionArtifactPublishRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new IngestionArtifactPublishResult(
            Succeeded: true,
            LatestPromoted: false,
            RunId: string.Empty,
            RunDirectory: string.Empty,
            ManifestPath: string.Empty,
            WikiDirectory: null,
            GraphMlPath: null,
            FailureReason: null));
    }
}
