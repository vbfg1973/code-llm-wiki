using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotRankingProjectionRequest(
    IReadOnlyList<SemanticTriple> Triples,
    DeclarationCatalog Declarations,
    StructuralMetricRollupCatalog StructuralMetrics,
    HotspotRankingOptions Options,
    int MaxDegreeOfParallelism);
