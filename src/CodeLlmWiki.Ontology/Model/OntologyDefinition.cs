namespace CodeLlmWiki.Ontology.Model;

public sealed record OntologyDefinition(string Version, IReadOnlyList<OntologyPredicate> Predicates);
