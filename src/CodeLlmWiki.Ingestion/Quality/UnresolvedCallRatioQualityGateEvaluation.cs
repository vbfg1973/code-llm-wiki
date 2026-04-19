namespace CodeLlmWiki.Ingestion.Quality;

public sealed record UnresolvedCallRatioQualityGateEvaluation(
    bool Passed,
    UnresolvedCallRatioQualityGateEvidence Evidence);
