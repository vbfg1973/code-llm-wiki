using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyInternalMethodUsageNode(
    EntityId InternalMethodId,
    string InternalMethodDisplayName,
    int UsageCount);
