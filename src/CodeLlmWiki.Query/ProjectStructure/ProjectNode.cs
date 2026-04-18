using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record ProjectNode(
    EntityId Id,
    string Name,
    string Path,
    string DiscoveryMethod,
    IReadOnlyList<EntityId> PackageIds);
