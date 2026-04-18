using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MethodDeclarationNode(
    EntityId Id,
    MethodDeclarationKind Kind,
    string Name,
    string DisplayName,
    string Signature,
    EntityId DeclaringTypeId,
    DeclarationAccessibility Accessibility,
    int Arity,
    IReadOnlyList<MethodParameterNode> Parameters,
    TypeReferenceNode? ReturnType,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsExtern,
    bool IsExtensionMethod,
    TypeReferenceNode? ExtendedType,
    IReadOnlyList<EntityId> DeclarationFileIds)
{
    public IReadOnlyList<DeclarationLocationNode> DeclarationLocations { get; init; } = [];
}
