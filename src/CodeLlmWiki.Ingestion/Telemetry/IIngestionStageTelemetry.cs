namespace CodeLlmWiki.Ingestion.Telemetry;

public interface IIngestionStageTelemetry
{
    IIngestionStageScope BeginStage(string stageId, IReadOnlyDictionary<string, long>? counters = null);
}
