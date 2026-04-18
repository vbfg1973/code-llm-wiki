namespace CodeLlmWiki.Cli.Config;

public sealed record IngestionCliConfig(string? OntologyPath, bool? AllowPartialSuccess)
{
    public static readonly IngestionCliConfig Empty = new(null, null);
}
