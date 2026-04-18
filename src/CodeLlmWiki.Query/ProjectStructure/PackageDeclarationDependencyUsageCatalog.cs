namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationDependencyUsageCatalog(
    int UsageCount,
    IReadOnlyList<PackageDeclarationDependencyNamespaceUsageNode> Namespaces)
{
    public static PackageDeclarationDependencyUsageCatalog Empty { get; } = new(0, []);
}
