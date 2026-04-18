using CodeLlmWiki.Ontology.Model;

namespace CodeLlmWiki.Ontology;

public interface IOntologyLoader
{
    Task<OntologyLoadResult> LoadAsync(string ontologyPath, CancellationToken cancellationToken);
}
