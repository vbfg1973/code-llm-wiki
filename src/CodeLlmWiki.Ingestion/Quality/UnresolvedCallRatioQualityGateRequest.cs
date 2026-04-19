namespace CodeLlmWiki.Ingestion.Quality;

public sealed record UnresolvedCallRatioQualityGateRequest(
    IReadOnlyList<IngestionDiagnostic> Diagnostics,
    int TotalCallResolutionAttempts,
    UnresolvedCallRatioQualityGatePolicy? Policy = null);
