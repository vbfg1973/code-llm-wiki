using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Ingestion;

public sealed record IngestionRunResult(
    IngestionRunStatus Status,
    int ExitCode,
    IReadOnlyList<IngestionDiagnostic> Diagnostics,
    EntityId RepositoryId,
    IReadOnlyList<SemanticTriple> Triples);
