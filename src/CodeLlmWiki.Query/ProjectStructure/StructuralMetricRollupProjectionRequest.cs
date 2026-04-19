using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricRollupProjectionRequest(
    EntityId RepositoryId,
    IReadOnlyList<SemanticTriple> Triples,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<FileNode> Files,
    DeclarationCatalog Declarations,
    StructuralMetricScopeFilter ScopeFilter,
    int MaxDegreeOfParallelism);
