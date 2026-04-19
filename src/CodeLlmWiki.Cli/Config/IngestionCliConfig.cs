using System.Text.Json.Serialization;

namespace CodeLlmWiki.Cli.Config;

public sealed record IngestionCliConfig(
    string? OntologyPath,
    bool? AllowPartialSuccess,
    string? OutputRoot,
    [property: JsonPropertyName("max_merge_entries_per_file")] int? MaxMergeEntriesPerFile,
    [property: JsonPropertyName("metric_computation_max_degree_of_parallelism")] int? MetricComputationMaxDegreeOfParallelism,
    [property: JsonPropertyName("semantic_call_max_degree_of_parallelism")] int? SemanticCallGraphMaxDegreeOfParallelism)
{
    public static readonly IngestionCliConfig Empty = new(null, null, null, null, null, null);
}
