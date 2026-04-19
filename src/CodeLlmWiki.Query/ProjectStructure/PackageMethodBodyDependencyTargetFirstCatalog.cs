namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageMethodBodyDependencyTargetFirstCatalog(
    int UsageCount,
    IReadOnlyList<PackageMethodBodyExternalTypeUsageNode> ExternalTypes)
{
    public static PackageMethodBodyDependencyTargetFirstCatalog Empty { get; } = new(0, []);
}
