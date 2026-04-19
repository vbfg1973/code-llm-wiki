using CodeLlmWiki.Ingestion.Diagnostics;
using CodeLlmWiki.Ingestion.Quality;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class DiagnosticsContractCompatibilityTests
{
    [Fact]
    public void CallResolutionDiagnosticCodes_All_IsStable()
    {
        Assert.Equal(
            new[]
            {
                "method:call:resolution:failed",
                "method:call:resolution:failed:symbol-unresolved",
                "method:call:resolution:failed:missing-containing-type",
                "method:call:internal-target-unmatched",
            },
            CallResolutionDiagnosticCodes.All);
    }

    [Fact]
    public void IngestionRunStatus_Names_AreStable()
    {
        Assert.Equal(
            new[]
            {
                "Succeeded",
                "SucceededWithDiagnostics",
                "Failed",
                "FailedQualityGate",
            },
            Enum.GetNames<IngestionRunStatus>());
    }

    [Fact]
    public void UnresolvedCallRatioQualityGate_Id_IsStable()
    {
        Assert.Equal("quality:unresolved-call-ratio", UnresolvedCallRatioQualityGateEvaluator.GateId);
    }
}
