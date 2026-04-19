using CodeLlmWiki.Ontology.Model;
using CodeLlmWiki.Ontology.Internal;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeLlmWiki.Ontology;

public sealed class OntologyLoader : IOntologyLoader
{
    private readonly IDeserializer _deserializer;

    public OntologyLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<OntologyLoadResult> LoadAsync(string ontologyPath, CancellationToken cancellationToken)
    {
        var issues = new List<OntologyValidationIssue>();

        if (string.IsNullOrWhiteSpace(ontologyPath))
        {
            issues.Add(new OntologyValidationIssue("ontology:path:missing", "Ontology path is required."));
            return new OntologyLoadResult(null, issues);
        }

        if (!File.Exists(ontologyPath))
        {
            issues.Add(new OntologyValidationIssue("ontology:path:not-found", $"Ontology file '{ontologyPath}' does not exist."));
            return new OntologyLoadResult(null, issues);
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(ontologyPath, cancellationToken);
            var dto = _deserializer.Deserialize<OntologyDocumentDto>(yaml) ?? new OntologyDocumentDto();

            var predicates = (dto.Predicates ?? [])
                .Select(x => new OntologyPredicate(x.Id?.Trim() ?? string.Empty))
                .ToList();

            var definition = new OntologyDefinition(dto.Version?.Trim() ?? string.Empty, predicates);
            Validate(definition, issues);

            return new OntologyLoadResult(issues.Count == 0 ? definition : null, issues);
        }
        catch (Exception ex)
        {
            issues.Add(new OntologyValidationIssue("ontology:parse:failed", ex.Message));
            return new OntologyLoadResult(null, issues);
        }
    }

    private static void Validate(OntologyDefinition definition, List<OntologyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(definition.Version))
        {
            issues.Add(new OntologyValidationIssue("ontology:version:missing", "Ontology version is required."));
        }

        for (var i = 0; i < definition.Predicates.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(definition.Predicates[i].Id))
            {
                issues.Add(new OntologyValidationIssue("ontology:predicate:id-missing", $"Predicate at index {i} has an empty id."));
            }
        }
    }
}
