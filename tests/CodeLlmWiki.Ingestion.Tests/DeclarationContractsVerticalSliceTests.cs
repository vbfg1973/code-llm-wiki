using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ontology;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class DeclarationContractsVerticalSliceTests
{
    [Fact]
    public async Task Ontology_ContainsDeclarationPredicates_AndStillValidates()
    {
        var ontologyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "ontology",
            "ontology.v1.yaml"));

        var loader = new OntologyLoader();
        var result = await loader.LoadAsync(ontologyPath, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Definition);

        var predicateIds = result.Definition!.Predicates.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("core:containsNamespace", predicateIds);
        Assert.Contains("core:containsType", predicateIds);
        Assert.Contains("core:containsMember", predicateIds);
        Assert.Contains("core:declaresNamespace", predicateIds);
        Assert.Contains("core:declaresType", predicateIds);
        Assert.Contains("core:declaresMember", predicateIds);
        Assert.Contains("core:declarationKind", predicateIds);
        Assert.Contains("core:typeKind", predicateIds);
        Assert.Contains("core:memberKind", predicateIds);
        Assert.Contains("core:accessibility", predicateIds);
        Assert.Contains("core:hasNamespace", predicateIds);
        Assert.Contains("core:hasDeclaringType", predicateIds);
        Assert.Contains("core:hasDeclaredType", predicateIds);
        Assert.Contains("core:hasDeclaredTypeText", predicateIds);
        Assert.Contains("core:resolutionStatus", predicateIds);
        Assert.Contains("core:arity", predicateIds);
        Assert.Contains("core:genericParameter", predicateIds);
        Assert.Contains("core:genericConstraint", predicateIds);
        Assert.Contains("core:inherits", predicateIds);
        Assert.Contains("core:implements", predicateIds);
    }

    [Fact]
    public void ProjectStructureWikiModel_DefaultsDeclarations_ToEmptyCatalog()
    {
        var model = new ProjectStructureWikiModel(
            new RepositoryNode(new EntityId("repository:sample"), "sample", ".", "main", "main"),
            [],
            [],
            [],
            [],
            []);

        Assert.NotNull(model.Declarations);
        Assert.Empty(model.Declarations.Namespaces);
        Assert.Empty(model.Declarations.Types);
        Assert.Empty(model.Declarations.Members);
    }

    [Fact]
    public void DeclarationIdentityRules_AreDeterministic()
    {
        var typeNaturalKey = DeclarationIdentityRules.CreateTypeNaturalKey(
            "Sample.Assembly",
            "Sample.Domain",
            "Order`1");

        var equivalentTypeNaturalKey = DeclarationIdentityRules.CreateTypeNaturalKey(
            "Sample.Assembly",
            "Sample.Domain",
            "Order`1");

        var differentTypeNaturalKey = DeclarationIdentityRules.CreateTypeNaturalKey(
            "Sample.Assembly",
            "Sample.Domain",
            "Order`2");

        Assert.Equal(typeNaturalKey, equivalentTypeNaturalKey);
        Assert.NotEqual(typeNaturalKey, differentTypeNaturalKey);
    }

    [Fact]
    public void DeclarationOrderingRules_SortDeterministically()
    {
        var unordered =
            new[]
            {
                new DeclarationOrderingSample("B.Namespace", "BType", "b-path", "b-id"),
                new DeclarationOrderingSample("A.Namespace", "ZType", "a-z-path", "a-z-id"),
                new DeclarationOrderingSample("A.Namespace", "AType", "a-a-path", "a-a-id"),
            };

        var ordered = unordered
            .OrderBy(
                x => DeclarationOrderingRules.GetDeterministicSortKey(
                    x.NamespaceName,
                    x.DisplayName,
                    x.Path,
                    x.StableId),
                StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("A.Namespace", ordered[0].NamespaceName);
        Assert.Equal("AType", ordered[0].DisplayName);
        Assert.Equal("ZType", ordered[1].DisplayName);
        Assert.Equal("B.Namespace", ordered[2].NamespaceName);
    }

    [Fact]
    public void Render_RemainsCompatible_WhenDeclarationsArePresent()
    {
        var repository = new RepositoryNode(new EntityId("repository:sample"), "sample", ".", "main", "main");
        var model = new ProjectStructureWikiModel(repository, [], [], [], [], [])
        {
            Declarations = new DeclarationCatalog(
                [
                    new NamespaceDeclarationNode(
                        new EntityId("namespace:Sample"),
                        "Sample",
                        "Sample",
                        null,
                        [],
                        [],
                        [])
                ],
                [
                    new TypeDeclarationNode(
                        new EntityId("type:Sample.Order"),
                        TypeDeclarationKind.Class,
                        "Order",
                        "Order",
                        "Sample.Order",
                        new EntityId("namespace:Sample"),
                        null,
                        false,
                        false,
                        DeclarationAccessibility.Public,
                        0,
                        [],
                        [],
                        [],
                        [],
                        [],
                        [])
                ],
                [])
        };

        var pages = new ProjectStructureWikiRenderer().Render(model);

        Assert.NotEmpty(pages);
        Assert.Contains(pages, x => x.RelativePath == "repositories/sample.md");
    }

    private sealed record DeclarationOrderingSample(
        string NamespaceName,
        string DisplayName,
        string Path,
        string StableId);
}
