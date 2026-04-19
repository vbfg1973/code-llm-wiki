using CodeLlmWiki.Ingestion.Diagnostics;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class CallResolutionDiagnosticClassifierTests
{
    [Fact]
    public void Classify_SymbolUnresolved_EmitsAggregateAndCauseSpecificDiagnostics()
    {
        var classifier = new CallResolutionDiagnosticClassifier();

        var diagnostics = classifier.Classify(new CallResolutionFailureContext(
            Cause: CallResolutionFailureCause.SymbolUnresolved,
            SourceMethodId: "method:abc",
            InvocationText: "UnknownApi.Missing()"));

        Assert.Collection(
            diagnostics,
            x => Assert.Equal("method:call:resolution:failed", x.Code),
            x => Assert.Equal("method:call:resolution:failed:symbol-unresolved", x.Code));
    }

    [Fact]
    public void Classify_MissingContainingType_EmitsAggregateAndCauseSpecificDiagnostics()
    {
        var classifier = new CallResolutionDiagnosticClassifier();

        var diagnostics = classifier.Classify(new CallResolutionFailureContext(
            Cause: CallResolutionFailureCause.MissingContainingType,
            SourceMethodId: "method:def",
            InvocationText: "target.Call()"));

        Assert.Collection(
            diagnostics,
            x => Assert.Equal("method:call:resolution:failed", x.Code),
            x => Assert.Equal("method:call:resolution:failed:missing-containing-type", x.Code));
    }
}
