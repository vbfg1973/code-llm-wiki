using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyDependencyMethodUsageNode(
    EntityId MethodId,
    string MethodSignature,
    int UsageCount);
