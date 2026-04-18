using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Ingestion;

public sealed record IngestionPipelineResult(
    IReadOnlyList<SemanticTriple> Triples,
    IReadOnlyList<IngestionDiagnostic> Diagnostics);
