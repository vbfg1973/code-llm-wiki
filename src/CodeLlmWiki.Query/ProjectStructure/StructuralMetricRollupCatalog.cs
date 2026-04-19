namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricRollupCatalog(
    RepositoryStructuralMetricRollupNode Repository,
    IReadOnlyList<ProjectStructuralMetricRollupNode> Projects,
    IReadOnlyList<NamespaceStructuralMetricRollupNode> Namespaces,
    IReadOnlyList<FileStructuralMetricRollupNode> Files,
    StructuralMetricScopeFilter EffectiveFilter)
{
    public static StructuralMetricRollupCatalog Empty { get; } = new(
        new RepositoryStructuralMetricRollupNode(
            default,
            new StructuralMetricScopeRollup(
                new StructuralMetricCoverage(0, 0, 0, 0, 0),
                new StructuralMetricStatistics(0, 0d, 0d, 0d, 0d, 0, 0d, 0d, 0d),
                StructuralMetricSeverity.None,
                IncludedInRanking: false)),
        [],
        [],
        [],
        StructuralMetricScopeFilter.ProductionDefault);
}
