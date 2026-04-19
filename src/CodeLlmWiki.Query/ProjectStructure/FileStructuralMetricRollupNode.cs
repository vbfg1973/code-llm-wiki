using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record FileStructuralMetricRollupNode(
    EntityId FileId,
    string Name,
    string Path,
    StructuralMetricCodeKind CodeKind,
    StructuralMetricScopeRollup Rollup);
