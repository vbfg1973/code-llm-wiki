using System.Globalization;
using CodeLlmWiki.Ingestion.Telemetry;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class StderrIngestionStageTelemetryTests
{
    [Fact]
    public void BeginStage_WritesDeterministicStartAndEndLines()
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var telemetry = new StderrIngestionStageTelemetry(writer);

        using (telemetry.BeginStage(
                   IngestionStageIds.ProjectDiscovery,
                   new Dictionary<string, long>
                   {
                       ["z_count"] = 2,
                       ["a_count"] = 1,
                   }))
        {
        }

        var lines = writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("ingest_stage|event=start|stage=project_discovery", lines[0]);
        Assert.Matches(@"^ingest_stage\|event=end\|stage=project_discovery\|elapsed_ms=\d+\|count\.a_count=1\|count\.z_count=2$", lines[1]);
    }

    [Fact]
    public void StageIds_AreStableAndComplete()
    {
        var expected = new[]
        {
            "project_discovery",
            "source_snapshot",
            "declaration_scan",
            "semantic_call_graph",
            "endpoint_extraction",
            "query_projection",
            "wiki_render",
            "graphml_serialize",
        };

        Assert.Equal(expected, IngestionStageIds.All);
    }
}
