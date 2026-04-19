namespace CodeLlmWiki.Ontology.Internal;

internal sealed class OntologyDocumentDto
{
    public string? Version { get; init; }

    public List<OntologyPredicateDto>? Predicates { get; init; }
}
