namespace CodeLlmWiki.Cli.Config;

public sealed record IngestionCliConfig(
    string? OntologyPath,
    bool? AllowPartialSuccess,
    string? OutputRoot)
{
    public static readonly IngestionCliConfig Empty = new(null, null, null);
}
