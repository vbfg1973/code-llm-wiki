namespace CodeLlmWiki.Ingestion.Diagnostics;

public sealed class CallResolutionDiagnosticClassifier : ICallResolutionDiagnosticClassifier
{
    public IReadOnlyList<IngestionDiagnostic> Classify(CallResolutionFailureContext failure)
    {
        var specificCode = failure.Cause switch
        {
            CallResolutionFailureCause.SymbolUnresolved => CallResolutionDiagnosticCodes.SymbolUnresolved,
            CallResolutionFailureCause.MissingContainingType => CallResolutionDiagnosticCodes.MissingContainingType,
            _ => CallResolutionDiagnosticCodes.SymbolUnresolved,
        };

        return
        [
            new IngestionDiagnostic(
                CallResolutionDiagnosticCodes.Aggregate,
                $"Failed to resolve invocation '{failure.InvocationText}' in method '{failure.SourceMethodId}'."),
            new IngestionDiagnostic(
                specificCode,
                $"Failed to resolve invocation '{failure.InvocationText}' in method '{failure.SourceMethodId}'."),
        ];
    }
}
