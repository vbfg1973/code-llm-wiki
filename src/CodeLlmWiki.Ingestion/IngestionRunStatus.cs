namespace CodeLlmWiki.Ingestion;

public enum IngestionRunStatus
{
    Succeeded = 0,
    SucceededWithDiagnostics = 1,
    Failed = 2,
    FailedQualityGate = 3,
}
