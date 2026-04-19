namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record ProjectStructureQueryOptions
{
    public static ProjectStructureQueryOptions Default { get; } = new();

    public StructuralMetricScopeFilter MetricScopeFilter { get; init; } = StructuralMetricScopeFilter.ProductionDefault;

    public HotspotRankingOptions HotspotRanking { get; init; } = HotspotRankingOptions.Default;
}
