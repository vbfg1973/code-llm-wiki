using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationExternalTypeUsageNode(
    EntityId ExternalTypeId,
    string ExternalTypeDisplayName,
    int UsageCount,
    IReadOnlyList<PackageDeclarationInternalTypeUsageNode> InternalTypes);
