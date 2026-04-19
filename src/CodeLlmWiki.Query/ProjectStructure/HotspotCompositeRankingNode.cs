namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotCompositeRankingNode(
    HotspotTargetKind TargetKind,
    IReadOnlyList<HotspotCompositeRankingRow> Rows);
