using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Contracts.Graph;
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
        Assert.Contains("core:declarationSourceLocation", predicateIds);
        Assert.Contains("core:resolutionStatus", predicateIds);
        Assert.Contains("core:arity", predicateIds);
        Assert.Contains("core:genericParameter", predicateIds);
        Assert.Contains("core:genericConstraint", predicateIds);
        Assert.Contains("core:inherits", predicateIds);
        Assert.Contains("core:implements", predicateIds);
        Assert.Contains("core:containsMethod", predicateIds);
        Assert.Contains("core:declaresMethod", predicateIds);
        Assert.Contains("core:methodKind", predicateIds);
        Assert.Contains("core:hasReturnType", predicateIds);
        Assert.Contains("core:hasReturnTypeText", predicateIds);
        Assert.Contains("core:hasMethodParameter", predicateIds);
        Assert.Contains("core:parameterOrdinal", predicateIds);
        Assert.Contains("core:parameterName", predicateIds);
        Assert.Contains("core:implementsMethod", predicateIds);
        Assert.Contains("core:overridesMethod", predicateIds);
        Assert.Contains("core:calls", predicateIds);
        Assert.Contains("core:readsProperty", predicateIds);
        Assert.Contains("core:writesProperty", predicateIds);
        Assert.Contains("core:readsField", predicateIds);
        Assert.Contains("core:writesField", predicateIds);
        Assert.Contains("core:isExtensionMethod", predicateIds);
        Assert.Contains("core:extendsType", predicateIds);
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
        Assert.Empty(model.Declarations.Methods.Declarations);
        Assert.Empty(model.Declarations.Methods.Relations);
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
    public void MethodIdentityRules_AreDeterministic()
    {
        var methodNaturalKey = DeclarationIdentityRules.CreateMethodNaturalKey(
            "Sample.Assembly",
            "type::Sample.Assembly::Sample.Domain::OrderService",
            "Save",
            ["System.String", "System.Threading.CancellationToken"],
            0);

        var equivalentMethodNaturalKey = DeclarationIdentityRules.CreateMethodNaturalKey(
            "Sample.Assembly",
            "type::Sample.Assembly::Sample.Domain::OrderService",
            "Save",
            ["System.String", "System.Threading.CancellationToken"],
            0);

        var differentMethodNaturalKey = DeclarationIdentityRules.CreateMethodNaturalKey(
            "Sample.Assembly",
            "type::Sample.Assembly::Sample.Domain::OrderService",
            "Save",
            ["System.String"],
            0);

        Assert.Equal(methodNaturalKey, equivalentMethodNaturalKey);
        Assert.NotEqual(methodNaturalKey, differentMethodNaturalKey);
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

    [Fact]
    public void Query_ProjectsMethodContracts_FromTriples()
    {
        var repositoryId = new EntityId("repository:sample");
        var typeId = new EntityId("type:Sample.OrderService");
        var methodId = new EntityId("method:Sample.OrderService.Save(System.String)");
        var methodInterfaceId = new EntityId("method:Sample.IOrderService.Save(System.String)");

        var triples =
            new[]
            {
                Triple(repositoryId, "core:entityType", "repository"),
                Triple(repositoryId, "core:hasName", "sample"),
                Triple(repositoryId, "core:hasPath", "."),
                Triple(repositoryId, "core:headBranch", "main"),
                Triple(repositoryId, "core:mainlineBranch", "main"),

                Triple(typeId, "core:entityType", "type-declaration"),
                Triple(typeId, "core:hasName", "OrderService"),
                Triple(typeId, "core:hasPath", "Sample.OrderService"),
                Triple(typeId, "core:typeKind", "class"),
                Triple(typeId, "core:accessibility", "public"),
                Triple(typeId, "core:arity", "0"),

                Triple(methodId, "core:entityType", "method-declaration"),
                Triple(methodId, "core:hasName", "Save"),
                Triple(methodId, "core:hasPath", "Sample.OrderService.Save(System.String)"),
                Triple(methodId, "core:methodKind", "method"),
                Triple(methodId, "core:accessibility", "public"),
                Triple(methodId, "core:arity", "0"),
                Triple(methodId, "core:hasReturnTypeText", "void"),

                Triple(methodInterfaceId, "core:entityType", "method-declaration"),
                Triple(methodInterfaceId, "core:hasName", "Save"),
                Triple(methodInterfaceId, "core:hasPath", "Sample.IOrderService.Save(System.String)"),
                Triple(methodInterfaceId, "core:methodKind", "method"),
                Triple(methodInterfaceId, "core:accessibility", "public"),
                Triple(methodInterfaceId, "core:arity", "0"),
                Triple(methodInterfaceId, "core:hasReturnTypeText", "void"),

                Edge(typeId, "core:containsMethod", methodId),
                Edge(methodId, "core:implementsMethod", methodInterfaceId),
            };

        var model = new ProjectStructureQueryService(triples).GetModel(repositoryId);

        var method = Assert.Single(model.Declarations.Methods.Declarations, x => x.Id == methodId);
        Assert.Equal(MethodDeclarationKind.Method, method.Kind);
        Assert.Equal("Save", method.Name);
        Assert.Equal("void", method.ReturnType?.DisplayText);

        var relation = Assert.Single(model.Declarations.Methods.Relations);
        Assert.Equal(MethodRelationKind.ImplementsMethod, relation.Kind);
        Assert.Equal(methodId, relation.SourceMethodId);
        Assert.Equal(methodInterfaceId, relation.TargetMethodId);

        var owningType = Assert.Single(model.Declarations.Types, x => x.Id == typeId);
        Assert.Contains(methodId, owningType.MethodIds);
    }

    private static SemanticTriple Triple(EntityId subjectId, string predicate, string value)
    {
        return new SemanticTriple(
            new EntityNode(subjectId),
            new PredicateId(predicate),
            new LiteralNode(value));
    }

    private static SemanticTriple Edge(EntityId subjectId, string predicate, EntityId objectId)
    {
        return new SemanticTriple(
            new EntityNode(subjectId),
            new PredicateId(predicate),
            new EntityNode(objectId));
    }

    private sealed record DeclarationOrderingSample(
        string NamespaceName,
        string DisplayName,
        string Path,
        string StableId);
}
