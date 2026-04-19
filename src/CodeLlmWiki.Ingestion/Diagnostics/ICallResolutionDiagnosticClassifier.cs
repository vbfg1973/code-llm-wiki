namespace CodeLlmWiki.Ingestion.Diagnostics;

public interface ICallResolutionDiagnosticClassifier
{
    IReadOnlyList<IngestionDiagnostic> Classify(CallResolutionFailureContext failure);
}
