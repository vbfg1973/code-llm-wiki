namespace CodeLlmWiki.Ingestion.Telemetry;

public interface IIngestionStageScope : IDisposable
{
    void SetCounters(IReadOnlyDictionary<string, long> counters);
}
