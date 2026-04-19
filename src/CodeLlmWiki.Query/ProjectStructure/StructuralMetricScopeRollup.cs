namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricScopeRollup(
    StructuralMetricCoverage Coverage,
    StructuralMetricStatistics Metrics,
    StructuralMetricSeverity Severity,
    bool IncludedInRanking);
