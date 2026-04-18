using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record TypeDeclarationNode(
    EntityId Id,
    TypeDeclarationKind Kind,
    string Name,
    string DisplayName,
    string Path,
    EntityId? NamespaceId,
    EntityId? DeclaringTypeId,
    bool IsPartialType,
    bool IsNestedType,
    DeclarationAccessibility Accessibility,
    int Arity,
    IReadOnlyList<string> GenericParameters,
    IReadOnlyList<string> GenericConstraints,
    IReadOnlyList<TypeReferenceNode> DirectBaseTypes,
    IReadOnlyList<TypeReferenceNode> DirectInterfaceTypes,
    IReadOnlyList<EntityId> MemberIds,
    IReadOnlyList<EntityId> DeclarationFileIds)
{
    public IReadOnlyList<DeclarationLocationNode> DeclarationLocations { get; init; } = [];
}
