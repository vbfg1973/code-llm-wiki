using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record UnknownDependencyNamespaceUsageNode(
    EntityId? NamespaceId,
    string NamespaceName,
    int UsageCount,
    IReadOnlyList<UnknownDependencyTypeUsageNode> Types);
