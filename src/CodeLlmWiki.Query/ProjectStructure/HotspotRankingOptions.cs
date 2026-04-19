namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotRankingOptions
{
    public static HotspotRankingOptions Default { get; } = new();

    public int? TopN { get; init; } = 25;

    public bool Unbounded { get; init; }

    public IReadOnlyDictionary<HotspotMetricKind, double> CompositeWeightOverrides { get; init; } =
        new Dictionary<HotspotMetricKind, double>();

    public IReadOnlyDictionary<HotspotMetricKind, HotspotSeverityThresholds> ThresholdOverrides { get; init; } =
        new Dictionary<HotspotMetricKind, HotspotSeverityThresholds>();
}
