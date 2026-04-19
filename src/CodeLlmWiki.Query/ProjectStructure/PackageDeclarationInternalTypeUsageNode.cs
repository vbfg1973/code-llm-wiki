using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationInternalTypeUsageNode(
    EntityId InternalTypeId,
    string InternalTypeName,
    int UsageCount);
