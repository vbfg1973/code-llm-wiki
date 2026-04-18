using CodeLlmWiki.Ingestion;

namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed record IngestionArtifactPublishRequest(
    string RepositoryPath,
    string OutputRootPath,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IngestionRunResult RunResult);
