using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record NamespaceStructuralMetricRollupNode(
    EntityId NamespaceId,
    string Name,
    string Path,
    bool IsGlobalNamespace,
    EntityId? ParentNamespaceId,
    StructuralMetricScopeRollup Direct,
    StructuralMetricScopeRollup Recursive);
