using CodeLlmWiki.Ingestion.Diagnostics;
using CodeLlmWiki.Ingestion.Quality;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class UnresolvedCallRatioQualityGateEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsFailed_WhenRatioExceedsThreshold()
    {
        var evaluator = new UnresolvedCallRatioQualityGateEvaluator();
        var diagnostics = new[]
        {
            new IngestionDiagnostic(CallResolutionDiagnosticCodes.Aggregate, "failed 1"),
            new IngestionDiagnostic(CallResolutionDiagnosticCodes.Aggregate, "failed 2"),
            new IngestionDiagnostic("method:call:internal-target-unmatched", "failed 3"),
            new IngestionDiagnostic("project:discovery:fallback", "warning"),
        };

        var evaluation = evaluator.Evaluate(new UnresolvedCallRatioQualityGateRequest(
            Diagnostics: diagnostics,
            TotalCallResolutionAttempts: 5,
            Policy: new UnresolvedCallRatioQualityGatePolicy(Threshold: 0.5)));

        Assert.False(evaluation.Passed);
        Assert.Equal("quality:unresolved-call-ratio", evaluation.Evidence.GateId);
        Assert.Equal(3, evaluation.Evidence.UnresolvedCallFailures);
        Assert.Equal(5, evaluation.Evidence.TotalCallResolutionAttempts);
        Assert.Equal(0.6d, evaluation.Evidence.UnresolvedCallRatio, 8);
        Assert.Equal(0.5d, evaluation.Evidence.Threshold, 8);
    }

    [Fact]
    public void Evaluate_ReturnsPassed_WhenRatioEqualsThreshold()
    {
        var evaluator = new UnresolvedCallRatioQualityGateEvaluator();
        var diagnostics = new[]
        {
            new IngestionDiagnostic(CallResolutionDiagnosticCodes.Aggregate, "failed 1"),
            new IngestionDiagnostic("method:call:internal-target-unmatched", "failed 2"),
        };

        var evaluation = evaluator.Evaluate(new UnresolvedCallRatioQualityGateRequest(
            Diagnostics: diagnostics,
            TotalCallResolutionAttempts: 4,
            Policy: new UnresolvedCallRatioQualityGatePolicy(Threshold: 0.5)));

        Assert.True(evaluation.Passed);
        Assert.Equal(0.5d, evaluation.Evidence.UnresolvedCallRatio, 8);
    }

    [Fact]
    public void Evaluate_ReturnsPassedWithZeroRatio_WhenNoCallAttempts()
    {
        var evaluator = new UnresolvedCallRatioQualityGateEvaluator();

        var evaluation = evaluator.Evaluate(new UnresolvedCallRatioQualityGateRequest(
            Diagnostics:
            [
                new IngestionDiagnostic(CallResolutionDiagnosticCodes.Aggregate, "failed 1"),
                new IngestionDiagnostic("method:call:internal-target-unmatched", "failed 2"),
            ],
            TotalCallResolutionAttempts: 0,
            Policy: new UnresolvedCallRatioQualityGatePolicy(Threshold: 0.1)));

        Assert.True(evaluation.Passed);
        Assert.Equal(0d, evaluation.Evidence.UnresolvedCallRatio, 8);
        Assert.Equal(2, evaluation.Evidence.UnresolvedCallFailures);
    }
}
