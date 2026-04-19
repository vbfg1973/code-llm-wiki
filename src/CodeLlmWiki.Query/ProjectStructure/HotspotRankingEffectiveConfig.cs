namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotRankingEffectiveConfig(
    int EffectiveTopN,
    bool Unbounded,
    IReadOnlyDictionary<HotspotMetricKind, double> CompositeWeights,
    IReadOnlyDictionary<HotspotMetricKind, HotspotSeverityThresholds> Thresholds);
