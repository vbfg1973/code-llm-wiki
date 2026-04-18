using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record TypeReferenceNode(
    EntityId? TypeId,
    string DisplayText,
    DeclarationResolutionStatus ResolutionStatus);
