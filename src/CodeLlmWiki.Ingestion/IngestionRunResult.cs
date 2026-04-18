using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Ingestion;

public sealed record IngestionRunResult(
    IngestionRunStatus Status,
    int ExitCode,
    IReadOnlyList<IngestionDiagnostic> Diagnostics,
    IReadOnlyList<SemanticTriple> Triples);
