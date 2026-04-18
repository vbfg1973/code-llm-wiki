using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageNode(
    EntityId Id,
    string Name,
    string CanonicalKey,
    IReadOnlyList<string> DeclaredVersions,
    IReadOnlyList<string> ResolvedVersions,
    IReadOnlyList<PackageProjectMembershipNode> ProjectMemberships)
{
    public PackageDeclarationDependencyUsageCatalog DeclarationDependencyUsage { get; init; } = PackageDeclarationDependencyUsageCatalog.Empty;
    public PackageMethodBodyDependencyUsageCatalog MethodBodyDependencyUsage { get; init; } = PackageMethodBodyDependencyUsageCatalog.Empty;
}
