namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageDeclarationDependencyTargetFirstCatalog(
    int UsageCount,
    IReadOnlyList<PackageDeclarationExternalTypeUsageNode> ExternalTypes)
{
    public static PackageDeclarationDependencyTargetFirstCatalog Empty { get; } = new(0, []);
}
