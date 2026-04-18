using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageNode(
    EntityId Id,
    string Name,
    string CanonicalKey,
    IReadOnlyList<string> DeclaredVersions,
    IReadOnlyList<string> ResolvedVersions,
    IReadOnlyList<PackageProjectMembershipNode> ProjectMemberships);
