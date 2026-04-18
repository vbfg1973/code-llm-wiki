using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;
using System.Diagnostics;

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

        Assert.True(pages.Count >= 7, $"Expected at least 7 pages but got {pages.Count}.");
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
    public async Task Render_PackagePage_IncludesDeclarationDependencyUsageSection_WhenUsageExists()
    {
        var fixture = await PackageDependencyFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var packagePage = pages.Single(page => page.RelativePath.StartsWith("packages/", StringComparison.Ordinal)
            && page.Markdown.Contains("# Package: Newtonsoft.Json", StringComparison.Ordinal));

        Assert.Contains("## Declaration Dependency Usage", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("SerializerFacade", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("- [[methods/", packagePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Normalize(", packagePage.Markdown, StringComparison.Ordinal);
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
            await File.WriteAllTextAsync(Path.Combine(withAssetsDir, "DependencyUsage.cs"),
                """
                namespace App.WithAssets;

                public sealed class SerializerFacade
                {
                    public Newtonsoft.Json.Linq.JToken Normalize(Newtonsoft.Json.Linq.JObject payload) => payload;
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
                                   </ItemGroup>
                                 </Project>
                                 """;
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssets.csproj"), noAssetsCsproj);
            await File.WriteAllTextAsync(Path.Combine(noAssetsDir, "NoAssetsUsage.cs"),
                """
                namespace App.NoAssets;

                public sealed class NoAssetsFacade
                {
                    public void Track(Serilog.ILogger logger) { }
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
