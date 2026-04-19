using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationDependencyNamespaceUsageNode(
    EntityId? NamespaceId,
    string NamespaceName,
    int UsageCount,
    IReadOnlyList<PackageDeclarationDependencyTypeUsageNode> Types);
