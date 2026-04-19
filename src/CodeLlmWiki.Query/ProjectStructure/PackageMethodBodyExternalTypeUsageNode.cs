using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyExternalTypeUsageNode(
    EntityId ExternalTypeId,
    string ExternalTypeDisplayName,
    int UsageCount,
    IReadOnlyList<PackageMethodBodyInternalMethodUsageNode> InternalMethods);
