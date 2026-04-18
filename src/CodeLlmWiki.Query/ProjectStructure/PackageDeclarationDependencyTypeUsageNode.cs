using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationDependencyTypeUsageNode(
    EntityId TypeId,
    string TypeName,
    int UsageCount,
    IReadOnlyList<PackageDeclarationDependencyMethodUsageNode> Methods);
