using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ontology.Model;

namespace CodeLlmWiki.Ingestion;

public sealed record IngestionExecutionContext(
    string RepositoryPath,
    EntityId RepositoryId,
    OntologyDefinition Ontology,
    int? SemanticCallGraphMaxDegreeOfParallelism = null);
