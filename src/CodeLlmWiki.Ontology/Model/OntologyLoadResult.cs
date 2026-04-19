namespace CodeLlmWiki.Ontology.Model;

public sealed record OntologyLoadResult(OntologyDefinition? Definition, IReadOnlyList<OntologyValidationIssue> Issues)
{
    public bool IsValid => Definition is not null && Issues.Count == 0;
}
