namespace CodeLlmWiki.Ingestion.Quality;

public sealed record UnresolvedCallRatioQualityGateEvidence(
    string GateId,
    int UnresolvedCallFailures,
    int TotalCallResolutionAttempts,
    double UnresolvedCallRatio,
    double Threshold,
    bool Passed);
