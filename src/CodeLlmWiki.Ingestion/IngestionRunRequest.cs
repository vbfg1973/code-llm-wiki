namespace CodeLlmWiki.Ingestion;

public sealed record IngestionRunRequest(
    string RepositoryPath,
    string? ConfigPath,
    string OntologyPath,
    bool AllowPartialSuccess,
    int? SemanticCallGraphMaxDegreeOfParallelism = null);
