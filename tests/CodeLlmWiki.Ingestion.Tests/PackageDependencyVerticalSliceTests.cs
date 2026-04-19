using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class PackageDependencyVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_DeclaredDependenciesAreRepresentedAndQueryable()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        Assert.Equal(3, model.Packages.Count);

        var withAssetsProject = model.Projects.Single(x => x.Name == "WithAssets");
        var withAssetsPackages = withAssetsProject.PackageIds
            .Select(id => model.Packages.Single(p => p.Id == id).Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Newtonsoft.Json"], withAssetsPackages);
    }

    [Fact]
    public async Task AnalyzeAsync_ResolvedVersionsAreCapturedWhenAvailable_WithDiagnosticsOtherwise()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var newtonsoft = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.Contains("13.0.3", newtonsoft.ResolvedVersions);

        var serilog = model.Packages.Single(x => x.Name == "Serilog");
        Assert.Empty(serilog.ResolvedVersions);

        Assert.Contains(analysis.Diagnostics, x => x.Code == "package:resolved:not-available");
    }

    [Fact]
    public async Task AnalyzeAsync_PackageMembershipIncludesPerProjectVersionContext()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var package = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.Equal("newtonsoft.json", package.CanonicalKey);
        Assert.Equal(2, package.ProjectMemberships.Count);

        var noAssets = package.ProjectMemberships.Single(x => x.ProjectName == "NoAssets");
        Assert.Equal("src/NoAssets/NoAssets.csproj", noAssets.ProjectPath);
        Assert.Equal("12.0.1", noAssets.DeclaredVersion);
        Assert.Null(noAssets.ResolvedVersion);

        var withAssets = package.ProjectMemberships.Single(x => x.ProjectName == "WithAssets");
        Assert.Equal("src/WithAssets/WithAssets.csproj", withAssets.ProjectPath);
        Assert.Equal("13.0.3", withAssets.DeclaredVersion);
        Assert.Equal("13.0.3", withAssets.ResolvedVersion);
    }

    [Fact]
    public async Task Render_GeneratesPackagePagesAndProjectLinks()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var renderer = new ProjectStructureWikiRenderer();
        var pages = renderer.Render(model);

        Assert.True(pages.Count >= 7, $"Expected at least 7 pages but got {pages.Count}.");
        Assert.Equal(3, pages.Count(x => x.RelativePath.StartsWith("packages/", StringComparison.Ordinal)));
        Assert.Contains(pages, page => page.RelativePath.StartsWith("projects/", StringComparison.Ordinal) && page.Markdown.Contains("[[packages/", StringComparison.Ordinal));
        Assert.Contains(pages, page => page.RelativePath == "index/repository-index.md");

        var packagePage = pages.Single(page => page.RelativePath.StartsWith("packages/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Package: Newtonsoft.Json", StringComparison.Ordinal));

        Assert.Contains("| project | project_path | declared_version | resolved_version |", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("| [NoAssets](projects/NoAssets.md) | `src/NoAssets/NoAssets.csproj` | `12.0.1` | `-` |", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("| [WithAssets](projects/WithAssets.md) | `src/WithAssets/WithAssets.csproj` | `13.0.3` | `13.0.3` |", packagePage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| [[projects/", packagePage.Markdown, StringComparison.Ordinal);

        var indexPage = pages.Single(page => page.RelativePath == "index/repository-index.md");
        Assert.Contains("[NoAssets](projects/NoAssets.md)", indexPage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| [[", indexPage.Markdown, StringComparison.Ordinal);

        var noAssetsIndex = packagePage.Markdown.IndexOf("NoAssets", StringComparison.Ordinal);
        var withAssetsIndex = packagePage.Markdown.IndexOf("WithAssets", StringComparison.Ordinal);
        Assert.True(noAssetsIndex >= 0 && withAssetsIndex >= 0 && noAssetsIndex < withAssetsIndex);
    }

    [Fact]
    public async Task Query_ProjectsPackageDeclarationDependencyUsage_ByNamespaceTypeAndMethod()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        Assert.Contains(analysis.Triples, x => x.Predicate == CorePredicates.HasReturnType);
        Assert.Contains(analysis.Triples, x => x.Predicate == CorePredicates.DependsOnTypeDeclaration);

        var package = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.True(package.DeclarationDependencyUsage.UsageCount > 0);
        Assert.NotEmpty(package.DeclarationDependencyUsage.Namespaces);

        var namespaceUsage = package.DeclarationDependencyUsage.Namespaces.Single(x => x.NamespaceName == "App.WithAssets");
        var typeUsage = namespaceUsage.Types.Single(x => x.TypeName == "SerializerFacade");
        Assert.Contains(typeUsage.Methods, x => x.MethodSignature.Contains("Normalize(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Query_ProjectsTargetFirstDeclarationDependencies_ByExternalTypeAndInternalType()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var package = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.True(package.DeclarationDependencyTargetFirst.UsageCount > 0);
        Assert.NotEmpty(package.DeclarationDependencyTargetFirst.ExternalTypes);

        var jToken = package.DeclarationDependencyTargetFirst.ExternalTypes
            .Single(x => x.ExternalTypeDisplayName == "Newtonsoft.Json.Linq.JToken");
        Assert.Contains(jToken.InternalTypes, x => x.InternalTypeName == "SerializerFacade");
    }

    [Fact]
    public async Task Render_PackagePage_UsesTargetFirstDeclarationDependencySection_WhenUsageExists()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var packagePage = pages.Single(page => page.RelativePath.StartsWith("packages/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Package: Newtonsoft.Json", StringComparison.Ordinal));

        Assert.Contains("## Declaration Dependencies (External Type -> Internal Type)", packagePage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Declaration Dependency Usage", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Newtonsoft.Json.Linq.JToken", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("SerializerFacade", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- [[types/App/WithAssets/SerializerFacade|SerializerFacade]]", packagePage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ProjectsPackageMethodBodyDependencyUsage_ByNamespaceTypeAndMethod_ExcludingNameof()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        Assert.Contains(analysis.Triples, x => x.Predicate == CorePredicates.DependsOnTypeInMethodBody);

        var package = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.True(package.MethodBodyDependencyUsage.UsageCount > 0);
        Assert.NotEmpty(package.MethodBodyDependencyUsage.Namespaces);

        var namespaceUsage = package.MethodBodyDependencyUsage.Namespaces.Single(x => x.NamespaceName == "App.WithAssets");
        var typeUsage = namespaceUsage.Types.Single(x => x.TypeName == "BodyUsageFacade");

        Assert.Contains(typeUsage.Methods, x => x.MethodSignature.Contains("Serialize(", StringComparison.Ordinal));
        Assert.DoesNotContain(typeUsage.Methods, x => x.MethodSignature.Contains("TokenName(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Query_ProjectsTargetFirstMethodBodyDependencies_ByExternalTypeAndInternalMethod()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var package = model.Packages.Single(x => x.Name == "Newtonsoft.Json");
        Assert.True(package.MethodBodyDependencyTargetFirst.UsageCount > 0);
        Assert.NotEmpty(package.MethodBodyDependencyTargetFirst.ExternalTypes);

        var jToken = package.MethodBodyDependencyTargetFirst.ExternalTypes
            .Single(x => x.ExternalTypeDisplayName == "Newtonsoft.Json.Linq.JToken");
        Assert.Contains(jToken.InternalMethods, x => x.InternalMethodDisplayName.Contains("BodyUsageFacade", StringComparison.Ordinal));
        Assert.DoesNotContain(jToken.InternalMethods, x => x.InternalMethodDisplayName.Contains("TokenName(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Render_PackagePage_UsesTargetFirstMethodBodyDependencySection_WhenUsageExists()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var packagePage = pages.Single(page => page.RelativePath.StartsWith("packages/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Package: Newtonsoft.Json", StringComparison.Ordinal));

        Assert.Contains("## Method Body Dependencies (External Type -> Internal Method)", packagePage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Method Body Dependency Usage", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Newtonsoft.Json.Linq.JToken", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("BodyUsageFacade", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- [[methods/", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Serialize(", packagePage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("TokenName(", packagePage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_MethodCalls_LinkExternalTargets_ThroughPackageDeepAnchors_WhenAttributionIsResolvable()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var serializeMethodPage = pages.Single(page =>
            page.RelativePath.StartsWith("methods/App/WithAssets/BodyUsageFacade/", StringComparison.Ordinal)
            && page.Markdown.Contains("method_name: Serialize", StringComparison.Ordinal));
        var packagePage = pages.Single(page => page.RelativePath == "packages/Newtonsoft.Json.md");

        Assert.Contains("## Calls", serializeMethodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("(packages/Newtonsoft.Json.md#ext-newtonsoft-json-linq-jtoken-", serializeMethodPage.Markdown, StringComparison.Ordinal);
        var match = Regex.Match(serializeMethodPage.Markdown, @"packages/Newtonsoft\.Json\.md#(?<anchor>[^\)\s]+)", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        var anchor = match.Groups["anchor"].Value;
        Assert.Contains($"<a id=\"{anchor}\"></a>", packagePage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_UsesProjectScopedAttribution_InMixedVersionRepository()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var newtonsoftJson = model.Packages.Single(x => x.Name == "Newtonsoft.Json");

        var declarationNamespaces = newtonsoftJson.DeclarationDependencyUsage.Namespaces
            .Select(x => x.NamespaceName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["App.NoAssets", "App.WithAssets"], declarationNamespaces);

        var methodBodyNamespaces = newtonsoftJson.MethodBodyDependencyUsage.Namespaces
            .Select(x => x.NamespaceName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["App.NoAssets", "App.WithAssets"], methodBodyNamespaces);

        var noAssetsMembership = newtonsoftJson.ProjectMemberships.Single(x => x.ProjectName == "NoAssets");
        var withAssetsMembership = newtonsoftJson.ProjectMemberships.Single(x => x.ProjectName == "WithAssets");
        Assert.Equal("12.0.1", noAssetsMembership.DeclaredVersion);
        Assert.Equal("13.0.3", withAssetsMembership.DeclaredVersion);
    }

    [Fact]
    public async Task Query_DoesNotMisattribute_ToPrefixOverlappingPackageOutsideSourceProject()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var prefixPackage = model.Packages.Single(x => x.Name == "Newtonsoft");
        Assert.Equal(0, prefixPackage.DeclarationDependencyUsage.UsageCount);
        Assert.Equal(0, prefixPackage.MethodBodyDependencyUsage.UsageCount);
    }

    [Fact]
    public async Task Query_ProjectsUnknownPackageAttribution_WhenProjectScopedMatchIsAmbiguous()
    {
        var fixture = await PackageDependencyFixture.CreateAsync(includeAmbiguousPackageInNoAssets: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        Assert.True(model.DependencyAttribution.DeclarationUnknown.UsageCount > 0);
        Assert.True(model.DependencyAttribution.MethodBodyUnknown.UsageCount > 0);

        var declarationMethodSignatures = model.DependencyAttribution.DeclarationUnknown.Namespaces
            .SelectMany(x => x.Types)
            .SelectMany(x => x.Methods)
            .Select(x => x.MethodSignature)
            .ToArray();
        Assert.Contains(declarationMethodSignatures, x => x.Contains("Normalize(", StringComparison.Ordinal));

        var declarationReasons = model.DependencyAttribution.DeclarationUnknown.Namespaces
            .SelectMany(x => x.Types)
            .SelectMany(x => x.Methods)
            .Select(x => x.AttributionReason)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ambiguous_project_scoped_match", declarationReasons);
    }

    [Fact]
    public async Task Query_ProjectsUnresolvedDependencyEntities_WithReasonCodes()
    {
        var fixture = await PackageDependencyFixture.CreateAsync(includeUnresolvedSignatureInNoAssets: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var unresolvedEntityTypes = analysis.Triples
            .Where(x => x.Predicate == CorePredicates.EntityType && x.Object is LiteralNode literal)
            .Select(x => ((LiteralNode)x.Object).Value?.ToString() ?? string.Empty)
            .ToArray();
        Assert.Contains("unresolved-type-reference", unresolvedEntityTypes);

        var unresolvedReasons = model.DependencyAttribution.DeclarationUnknown.Namespaces
            .SelectMany(x => x.Types)
            .SelectMany(x => x.Methods)
            .Select(x => x.TargetResolutionReason)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        Assert.Contains("type-resolution-fallback", unresolvedReasons);
    }

    [Fact]
    public async Task Render_IncludesUnknownPackageAttributionPage_WhenUnknownUsageExists()
    {
        var fixture = await PackageDependencyFixture.CreateAsync(includeAmbiguousPackageInNoAssets: true, includeUnresolvedSignatureInNoAssets: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var unknownPage = pages.Single(page => page.RelativePath == "packages/unknown-package-attribution.md");

        Assert.Contains("## Declaration Dependency Usage (Unknown Package Attribution)", unknownPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Method Body Dependency Usage (Unknown Package Attribution)", unknownPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("ambiguous_project_scoped_match", unknownPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("type-resolution-fallback", unknownPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_UnknownPackageAttributionPage_IsDeterministicAcrossRuns()
    {
        var fixture = await PackageDependencyFixture.CreateAsync(includeAmbiguousPackageInNoAssets: true, includeUnresolvedSignatureInNoAssets: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());

        var first = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var firstModel = new ProjectStructureQueryService(first.Triples).GetModel(first.RepositoryId);
        var firstPage = new ProjectStructureWikiRenderer()
            .Render(firstModel)
            .Single(page => page.RelativePath == "packages/unknown-package-attribution.md");

        var second = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var secondModel = new ProjectStructureQueryService(second.Triples).GetModel(second.RepositoryId);
        var secondPage = new ProjectStructureWikiRenderer()
            .Render(secondModel)
            .Single(page => page.RelativePath == "packages/unknown-package-attribution.md");

        Assert.Equal(firstPage.Markdown, secondPage.Markdown);
    }

    [Fact]
    public async Task Query_TypeDependencyRollups_PreserveDeclarationAndMethodBodySplit()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var declarationOnlyType = model.Declarations.Types.Single(x => x.Name == "SerializerFacade");
        Assert.Contains(declarationOnlyType.DependencyRollup.DeclarationPackages, x => x.PackageName == "Newtonsoft.Json");
        Assert.Empty(declarationOnlyType.DependencyRollup.MethodBodyPackages);

        var methodBodyType = model.Declarations.Types.Single(x => x.Name == "BodyUsageFacade");
        Assert.Empty(methodBodyType.DependencyRollup.DeclarationPackages);
        Assert.Contains(methodBodyType.DependencyRollup.MethodBodyPackages, x => x.PackageName == "Newtonsoft.Json");
    }

    [Fact]
    public async Task Query_TypeDependencyRollups_TrackUnknownAttributionCounts()
    {
        var fixture = await PackageDependencyFixture.CreateAsync(includeAmbiguousPackageInNoAssets: true, includeUnresolvedSignatureInNoAssets: true);
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var ambiguousType = model.Declarations.Types.Single(x => x.Name == "NoAssetsJsonFacade");
        Assert.True(ambiguousType.DependencyRollup.DeclarationUnknownUsageCount > 0);
        Assert.True(ambiguousType.DependencyRollup.MethodBodyUnknownUsageCount > 0);

        var unresolvedType = model.Declarations.Types.Single(x => x.Name == "NoAssetsUnresolvedFacade");
        Assert.True(unresolvedType.DependencyRollup.DeclarationUnknownUsageCount > 0);
    }

    [Fact]
    public async Task Render_TypePage_IncludesDependencyRollupSections()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var typePage = pages.Single(page => page.RelativePath.StartsWith("types/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Type: BodyUsageFacade", StringComparison.Ordinal));

        Assert.Contains("## Dependency Rollup", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Declaration Packages", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Method Body Packages", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[packages/", typePage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ontology_ContainsPackagePredicates_AndStillValidates()
    {
        var ontologyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ontology", "ontology.v1.yaml"));
        var loader = new OntologyLoader();
        var result = await loader.LoadAsync(ontologyPath, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Definition);

        var predicateIds = result.Definition!.Predicates.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("core:referencesPackage", predicateIds);
        Assert.Contains("core:hasPackageReference", predicateIds);
        Assert.Contains("core:hasDeclaredVersion", predicateIds);
        Assert.Contains("core:hasResolvedVersion", predicateIds);
        Assert.Contains("core:targetFramework", predicateIds);
        Assert.Contains("core:dependsOnTypeDeclaration", predicateIds);
        Assert.Contains("core:dependsOnTypeInMethodBody", predicateIds);
    }

    private sealed class PackageDependencyFixture
    {
        private PackageDependencyFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<PackageDependencyFixture> CreateAsync(
            bool includeAmbiguousPackageInNoAssets = false,
            bool includeUnresolvedSignatureInNoAssets = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-packages-{Guid.NewGuid():N}", "package-repo");
            Directory.CreateDirectory(root);

            var solutionPath = Path.Combine(root, "Sample.slnx");
            var slnx = """
                       <Solution>
                         <Project Path="src/WithAssets/WithAssets.csproj" />
                         <Project Path="src/NoAssets/NoAssets.csproj" />
                         <Project Path="src/PrefixOnly/PrefixOnly.csproj" />
                       </Solution>
                       """;
            await File.WriteAllTextAsync(solutionPath, slnx);

            var withAssetsDir = Path.Combine(root, "src", "WithAssets");
            Directory.CreateDirectory(withAssetsDir);
            var withAssetsCsproj = """
                                   <Project Sdk="Microsoft.NET.Sdk">
                                     <PropertyGroup>
                                       <TargetFramework>net10.0</TargetFramework>
                                     </PropertyGroup>
                                     <ItemGroup>
                                       <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                                     </ItemGroup>
                                   </Project>
                                   """;
            await File.WriteAllTextAsync(Path.Combine(withAssetsDir, "WithAssets.csproj"), withAssetsCsproj);
            await File.WriteAllTextAsync(Path.Combine(withAssetsDir, "DependencyUsage.cs"),
                """
                namespace App.WithAssets;

                public sealed class SerializerFacade
                {
                    public Newtonsoft.Json.Linq.JToken Normalize(Newtonsoft.Json.Linq.JObject payload) => payload;
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(withAssetsDir, "BodyUsage.cs"),
                """
                namespace App.WithAssets;

                public sealed class BodyUsageFacade
                {
                    public string Serialize(object payload)
                    {
                        var token = Newtonsoft.Json.Linq.JToken.FromObject(payload);
                        return token.ToString();
                    }

                    public string TokenName()
                    {
                        return nameof(Newtonsoft.Json.Linq.JToken);
                    }
                }
                """);

            var withAssetsObj = Path.Combine(withAssetsDir, "obj");
            Directory.CreateDirectory(withAssetsObj);
            var assets = """
                         {
                           "libraries": {
                             "Newtonsoft.Json/13.0.3": {
                               "type": "package"
                             }
                           }
                         }
                         """;
            await File.WriteAllTextAsync(Path.Combine(withAssetsObj, "project.assets.json"), assets);

            var noAssetsDir = Path.Combine(root, "src", "NoAssets");
            Directory.CreateDirectory(noAssetsDir);
            var noAssetsCsproj = """
                                 <Project Sdk="Microsoft.NET.Sdk">
                                   <PropertyGroup>
                                     <TargetFramework>net10.0</TargetFramework>
                                   </PropertyGroup>
                                   <ItemGroup>
                                     <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
                                     <PackageReference Include="Serilog" Version="3.1.0" />
                                     __NO_ASSETS_OPTIONAL_PACKAGE__
                                   </ItemGroup>
                                 </Project>
                                 """;
            if (includeAmbiguousPackageInNoAssets)
            {
                noAssetsCsproj = noAssetsCsproj.Replace("__NO_ASSETS_OPTIONAL_PACKAGE__", "<PackageReference Include=\"Newtonsoft\" Version=\"1.0.0\" />", StringComparison.Ordinal);
            }
            else
            {
                noAssetsCsproj = noAssetsCsproj.Replace("__NO_ASSETS_OPTIONAL_PACKAGE__", string.Empty, StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssets.csproj"), noAssetsCsproj);
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssetsUsage.cs"),
                """
                namespace App.NoAssets;

                public sealed class NoAssetsFacade
                {
                    public void Track(Serilog.ILogger logger) { }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssetsJsonUsage.cs"),
                """
                namespace App.NoAssets;

                public sealed class NoAssetsJsonFacade
                {
                    public Newtonsoft.Json.Linq.JToken Normalize(Newtonsoft.Json.Linq.JObject payload)
                    {
                        return Newtonsoft.Json.Linq.JToken.FromObject(payload);
                    }
                }
                """);
            if (includeUnresolvedSignatureInNoAssets)
            {
                await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssetsUnresolvedUsage.cs"),
                    """
                    namespace App.NoAssets;

                    public sealed class NoAssetsUnresolvedFacade
                    {
                        public Missing[] Resolve(Missing[] payload) => payload;
                    }
                    """);
            }

            var prefixOnlyDir = Path.Combine(root, "src", "PrefixOnly");
            Directory.CreateDirectory(prefixOnlyDir);
            var prefixOnlyCsproj = """
                                   <Project Sdk="Microsoft.NET.Sdk">
                                     <PropertyGroup>
                                       <TargetFramework>net10.0</TargetFramework>
                                     </PropertyGroup>
                                     <ItemGroup>
                                       <PackageReference Include="Newtonsoft" Version="1.0.0" />
                                     </ItemGroup>
                                   </Project>
                                   """;
            await File.WriteAllTextAsync(Path.Combine(prefixOnlyDir, "PrefixOnly.csproj"), prefixOnlyCsproj);
            await File.WriteAllTextAsync(Path.Combine(prefixOnlyDir, "PrefixOnlyUsage.cs"),
                """
                namespace App.PrefixOnly;

                public sealed class PrefixOnlyFacade
                {
                    public string Name() => nameof(PrefixOnlyFacade);
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new PackageDependencyFixture(root);
        }

        private static void RunGit(string workingDirectory, params string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo)!;
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdOut}\n{stdErr}");
            }
        }
    }
}
