using System.Diagnostics;
using System.Text;

namespace CodeLlmWiki.Ingestion.Telemetry;

public sealed class StderrIngestionStageTelemetry : IIngestionStageTelemetry
{
    private readonly TextWriter _writer;
    private readonly object _sync = new();

    public StderrIngestionStageTelemetry(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public IIngestionStageScope BeginStage(string stageId, IReadOnlyDictionary<string, long>? counters = null)
    {
        var normalizedStageId = Normalize(stageId);
        WriteLine($"ingest_stage|event=start|stage={normalizedStageId}");
        return new Scope(this, normalizedStageId, Stopwatch.GetTimestamp(), counters);
    }

    private void WriteEndLine(
        string stageId,
        long startedTimestamp,
        IReadOnlyDictionary<string, long>? counters)
    {
        var elapsedMs = (long)Math.Max(0, Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
        var builder = new StringBuilder($"ingest_stage|event=end|stage={stageId}|elapsed_ms={elapsedMs}");

        if (counters is not null && counters.Count > 0)
        {
            foreach (var pair in counters.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var escapedKey = NormalizeCounterKey(pair.Key);
                builder.Append("|count.");
                builder.Append(escapedKey);
                builder.Append('=');
                builder.Append(pair.Value);
            }
        }

        WriteLine(builder.ToString());
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown_stage";
        }

        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string NormalizeCounterKey(string value)
    {
        return Normalize(value).Replace(".", "_", StringComparison.Ordinal);
    }

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private sealed class Scope : IIngestionStageScope
    {
        private readonly StderrIngestionStageTelemetry _owner;
        private readonly string _stageId;
        private readonly long _startedTimestamp;
        private IReadOnlyDictionary<string, long>? _counters;
        private bool _disposed;

        public Scope(
            StderrIngestionStageTelemetry owner,
            string stageId,
            long startedTimestamp,
            IReadOnlyDictionary<string, long>? counters)
        {
            _owner = owner;
            _stageId = stageId;
            _startedTimestamp = startedTimestamp;
            _counters = counters;
        }

        public void SetCounters(IReadOnlyDictionary<string, long> counters)
        {
            _counters = counters;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.WriteEndLine(_stageId, _startedTimestamp, _counters);
        }
    }
}
