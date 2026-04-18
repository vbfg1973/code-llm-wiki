using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageNode(
    EntityId Id,
    string Name,
    IReadOnlyList<string> DeclaredVersions,
    IReadOnlyList<string> ResolvedVersions);
