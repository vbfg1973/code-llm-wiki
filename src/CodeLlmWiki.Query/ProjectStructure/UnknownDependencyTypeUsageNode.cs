using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record UnknownDependencyTypeUsageNode(
    EntityId TypeId,
    string TypeName,
    int UsageCount,
    IReadOnlyList<UnknownDependencyMethodUsageNode> Methods);
