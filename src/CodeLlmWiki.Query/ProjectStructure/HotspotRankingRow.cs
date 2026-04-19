using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotRankingRow(
    EntityId EntityId,
    string DisplayName,
    string Path,
    double RawValue,
    double NormalizedScore,
    HotspotSeverityBand Severity,
    int Rank);
