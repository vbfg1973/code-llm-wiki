namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyDependencyUsageCatalog(
    int UsageCount,
    IReadOnlyList<PackageMethodBodyDependencyNamespaceUsageNode> Namespaces)
{
    public static PackageMethodBodyDependencyUsageCatalog Empty { get; } = new(0, []);
}
