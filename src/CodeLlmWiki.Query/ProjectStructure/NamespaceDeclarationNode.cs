using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record NamespaceDeclarationNode(
    EntityId Id,
    string Name,
    string Path,
    EntityId? ParentNamespaceId,
    IReadOnlyList<EntityId> ChildNamespaceIds,
    IReadOnlyList<EntityId> ContainedTypeIds,
    IReadOnlyList<EntityId> DeclarationFileIds);
