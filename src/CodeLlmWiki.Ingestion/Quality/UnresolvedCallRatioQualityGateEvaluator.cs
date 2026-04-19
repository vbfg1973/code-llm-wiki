using CodeLlmWiki.Ingestion.Diagnostics;

namespace CodeLlmWiki.Ingestion.Quality;

public sealed class UnresolvedCallRatioQualityGateEvaluator : IUnresolvedCallRatioQualityGateEvaluator
{
    public const string GateId = "quality:unresolved-call-ratio";
    private const string InternalTargetUnmatchedCode = "method:call:internal-target-unmatched";

    public UnresolvedCallRatioQualityGateEvaluation Evaluate(UnresolvedCallRatioQualityGateRequest request)
    {
        var policy = request.Policy ?? UnresolvedCallRatioQualityGatePolicy.Default;
        var threshold = NormalizeThreshold(policy.Threshold);
        var unresolvedCallFailures = request.Diagnostics.Count(IsUnresolvedCallFailureDiagnostic);
        var totalCallResolutionAttempts = Math.Max(0, request.TotalCallResolutionAttempts);
        var unresolvedCallRatio = totalCallResolutionAttempts == 0
            ? 0d
            : (double)unresolvedCallFailures / totalCallResolutionAttempts;

        var passed = totalCallResolutionAttempts == 0 || unresolvedCallRatio <= threshold;
        var evidence = new UnresolvedCallRatioQualityGateEvidence(
            GateId: GateId,
            UnresolvedCallFailures: unresolvedCallFailures,
            TotalCallResolutionAttempts: totalCallResolutionAttempts,
            UnresolvedCallRatio: unresolvedCallRatio,
            Threshold: threshold,
            Passed: passed);

        return new UnresolvedCallRatioQualityGateEvaluation(Passed: passed, Evidence: evidence);
    }

    private static bool IsUnresolvedCallFailureDiagnostic(IngestionDiagnostic diagnostic)
    {
        return string.Equals(diagnostic.Code, CallResolutionDiagnosticCodes.Aggregate, StringComparison.Ordinal)
               || string.Equals(diagnostic.Code, InternalTargetUnmatchedCode, StringComparison.Ordinal);
    }

    private static double NormalizeThreshold(double threshold)
    {
        if (double.IsNaN(threshold) || double.IsInfinity(threshold))
        {
            return UnresolvedCallRatioQualityGatePolicy.DefaultThreshold;
        }

        return Math.Clamp(threshold, 0d, 1d);
    }
}
