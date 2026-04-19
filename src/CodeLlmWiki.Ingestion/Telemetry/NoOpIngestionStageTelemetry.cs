namespace CodeLlmWiki.Ingestion.Telemetry;

public sealed class NoOpIngestionStageTelemetry : IIngestionStageTelemetry
{
    public static readonly NoOpIngestionStageTelemetry Instance = new();

    private NoOpIngestionStageTelemetry()
    {
    }

    public IIngestionStageScope BeginStage(string stageId, IReadOnlyDictionary<string, long>? counters = null)
    {
        return NoOpIngestionStageScope.Instance;
    }

    private sealed class NoOpIngestionStageScope : IIngestionStageScope
    {
        public static readonly NoOpIngestionStageScope Instance = new();

        private NoOpIngestionStageScope()
        {
        }

        public void SetCounters(IReadOnlyDictionary<string, long> counters)
        {
        }

        public void Dispose()
        {
        }
    }
}
