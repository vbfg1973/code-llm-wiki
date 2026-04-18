using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MethodRelationNode(
    EntityId SourceMethodId,
    MethodRelationKind Kind,
    EntityId? TargetMethodId,
    EntityId? TargetMemberId,
    TypeReferenceNode? ExternalTargetType,
    string? ExternalAssemblyName,
    DeclarationResolutionStatus ResolutionStatus,
    string? ResolutionReason = null);
