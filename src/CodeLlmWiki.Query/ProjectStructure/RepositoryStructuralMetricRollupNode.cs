using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record RepositoryStructuralMetricRollupNode(
    EntityId RepositoryId,
    StructuralMetricScopeRollup Rollup);
