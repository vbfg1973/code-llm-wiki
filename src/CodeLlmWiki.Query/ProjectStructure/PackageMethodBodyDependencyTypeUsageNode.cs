using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyDependencyTypeUsageNode(
    EntityId TypeId,
    string TypeName,
    int UsageCount,
    IReadOnlyList<PackageMethodBodyDependencyMethodUsageNode> Methods);
