using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

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

        Assert.Equal(2, model.Packages.Count);

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

        Assert.Equal(7, pages.Count);
        Assert.Equal(2, pages.Count(x => x.RelativePath.StartsWith("packages/", StringComparison.Ordinal)));
        Assert.Contains(pages, page => page.RelativePath.StartsWith("projects/", StringComparison.Ordinal) && page.Markdown.Contains("[[packages/", StringComparison.Ordinal));
        Assert.Contains(pages, page => page.RelativePath == "index/repository-index.md");

        var packagePage = pages.Single(page => page.RelativePath.StartsWith("packages/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Package: Newtonsoft.Json", StringComparison.Ordinal));

        Assert.Contains("| project | project_path | declared_version | resolved_version |", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("|NoAssets]] | `src/NoAssets/NoAssets.csproj` | `12.0.1` | `-` |", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("|WithAssets]] | `src/WithAssets/WithAssets.csproj` | `13.0.3` | `13.0.3` |", packagePage.Markdown, StringComparison.Ordinal);

        var noAssetsIndex = packagePage.Markdown.IndexOf("NoAssets", StringComparison.Ordinal);
        var withAssetsIndex = packagePage.Markdown.IndexOf("WithAssets", StringComparison.Ordinal);
        Assert.True(noAssetsIndex >= 0 && withAssetsIndex >= 0 && noAssetsIndex < withAssetsIndex);
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
    }

    private sealed class PackageDependencyFixture
    {
        private PackageDependencyFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<PackageDependencyFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-packages-{Guid.NewGuid():N}", "package-repo");
            Directory.CreateDirectory(root);

            var solutionPath = Path.Combine(root, "Sample.slnx");
            var slnx = """
                       <Solution>
                         <Project Path="src/WithAssets/WithAssets.csproj" />
                         <Project Path="src/NoAssets/NoAssets.csproj" />
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
                                   </ItemGroup>
                                 </Project>
                                 """;
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssets.csproj"), noAssetsCsproj);

            return new PackageDependencyFixture(root);
        }
    }
}
