namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotMetricRankingNode(
    HotspotTargetKind TargetKind,
    HotspotMetricKind MetricKind,
    IReadOnlyList<HotspotRankingRow> Rows);
