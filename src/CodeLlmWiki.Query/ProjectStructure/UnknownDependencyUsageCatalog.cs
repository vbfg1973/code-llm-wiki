namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record UnknownDependencyUsageCatalog(
    int UsageCount,
    IReadOnlyList<UnknownDependencyNamespaceUsageNode> Namespaces)
{
    public static UnknownDependencyUsageCatalog Empty { get; } = new(0, []);
}
