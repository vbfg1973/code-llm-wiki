using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record EndpointNode(
    EntityId Id,
    string Family,
    string Kind,
    string Name,
    string CanonicalSignature,
    string HttpMethod,
    string AuthoredRouteTemplate,
    string NormalizedRouteKey,
    EndpointConfidence Confidence,
    string RuleId,
    string RuleVersion,
    string RuleSource,
    EntityId DeclaringMethodId,
    EntityId DeclaringTypeId,
    EntityId? NamespaceId,
    EntityId? GroupId,
    IReadOnlyList<EntityId> DeclarationFileIds);
