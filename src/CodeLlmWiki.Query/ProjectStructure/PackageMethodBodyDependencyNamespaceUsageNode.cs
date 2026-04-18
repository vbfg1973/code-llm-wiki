using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyDependencyNamespaceUsageNode(
    EntityId? NamespaceId,
    string NamespaceName,
    int UsageCount,
    IReadOnlyList<PackageMethodBodyDependencyTypeUsageNode> Types);
