using System.Text.Json.Serialization;

namespace CodeLlmWiki.Cli.Config;

public sealed record IngestionCliConfig(
    string? OntologyPath,
    bool? AllowPartialSuccess,
    string? OutputRoot,
    [property: JsonPropertyName("max_merge_entries_per_file")] int? MaxMergeEntriesPerFile)
{
    public static readonly IngestionCliConfig Empty = new(null, null, null, null);
}
