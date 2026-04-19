using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageProjectMembershipNode(
    EntityId ProjectId,
    string ProjectName,
    string ProjectPath,
    string? DeclaredVersion,
    string? ResolvedVersion);
