namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record TypeDependencyRollupCatalog(
    IReadOnlyList<TypeDependencyPackageUsageNode> DeclarationPackages,
    IReadOnlyList<TypeDependencyPackageUsageNode> MethodBodyPackages,
    int DeclarationUnknownUsageCount,
    int MethodBodyUnknownUsageCount)
{
    public static TypeDependencyRollupCatalog Empty { get; } = new([], [], 0, 0);
}
