namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotRankingCatalog(
    HotspotRankingEffectiveConfig EffectiveConfig,
    IReadOnlyList<HotspotMetricRankingNode> PrimaryRankings,
    IReadOnlyList<HotspotCompositeRankingNode> CompositeRankings)
{
    public static HotspotRankingCatalog Empty { get; } = new(
        new HotspotRankingEffectiveConfig(
            EffectiveTopN: 25,
            Unbounded: false,
            CompositeWeights: new Dictionary<HotspotMetricKind, double>(),
            Thresholds: new Dictionary<HotspotMetricKind, HotspotSeverityThresholds>()),
        [],
        []);
}
