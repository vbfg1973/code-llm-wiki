using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record TypeDependencyPackageUsageNode(
    EntityId PackageId,
    string PackageName,
    int UsageCount);
