using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed record ProjectStructureAnalysisResult(
    EntityId RepositoryId,
    IReadOnlyList<SemanticTriple> Triples,
    IReadOnlyList<IngestionDiagnostic> Diagnostics);
