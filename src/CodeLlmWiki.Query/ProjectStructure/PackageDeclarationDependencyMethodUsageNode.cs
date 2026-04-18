using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationDependencyMethodUsageNode(
    EntityId MethodId,
    string MethodSignature,
    int UsageCount);
