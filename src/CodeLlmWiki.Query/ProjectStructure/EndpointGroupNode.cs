using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record EndpointGroupNode(
    EntityId Id,
    string Family,
    string Name,
    string CanonicalKey,
    string AuthoredRoutePrefix,
    string NormalizedRoutePrefix,
    EntityId? DeclaringTypeId,
    EntityId? NamespaceId,
    IReadOnlyList<EntityId> DeclarationFileIds,
    IReadOnlyList<EntityId> EndpointIds);
