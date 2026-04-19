using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record ProjectStructuralMetricRollupNode(
    EntityId ProjectId,
    string Name,
    string Path,
    StructuralMetricCodeKind CodeKind,
    StructuralMetricScopeRollup Rollup);
