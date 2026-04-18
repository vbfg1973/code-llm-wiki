namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed record IngestionArtifactPublishResult(
    bool Succeeded,
    bool LatestPromoted,
    string RunId,
    string RunDirectory,
    string ManifestPath,
    string? WikiDirectory,
    string? GraphMlPath,
    string? FailureReason);
