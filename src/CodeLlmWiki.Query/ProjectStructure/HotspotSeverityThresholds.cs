namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record HotspotSeverityThresholds(
    double Low,
    double Medium,
    double High,
    double Critical);
