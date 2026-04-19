using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotCompositeRankingRow(
    EntityId EntityId,
    string DisplayName,
    string Path,
    double CompositeScore,
    HotspotSeverityBand Severity,
    int Rank);
