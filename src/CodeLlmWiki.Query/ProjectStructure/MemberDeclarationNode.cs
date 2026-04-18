using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MemberDeclarationNode(
    EntityId Id,
    MemberDeclarationKind Kind,
    string Name,
    string DisplayName,
    EntityId DeclaringTypeId,
    DeclarationAccessibility Accessibility,
    TypeReferenceNode? DeclaredType,
    string? ConstantValue,
    IReadOnlyList<EntityId> DeclarationFileIds);
