using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record UnknownDependencyMethodUsageNode(
    EntityId MethodId,
    string MethodSignature,
    string TargetTypeName,
    string AttributionReason,
    string TargetResolutionReason,
    int UsageCount);
