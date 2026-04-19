using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class ProjectStructureVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_IngestsRepositorySolutionsAndProjectsIntoTriples()
    {
        var fixture = await ProjectStructureFixture.CreateAsync();

        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        Assert.NotEqual(default, analysis.RepositoryId);
        Assert.NotEmpty(analysis.Triples);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        Assert.Equal("sample-repo", model.Repository.Name);
        Assert.Single(model.Solutions);
        Assert.Equal(2, model.Projects.Count);
        Assert.Contains(model.Projects, project => project.TargetFrameworks.Contains("net10.0", StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_UsesMsBuildFirstAndFallsBackWithDiagnostics()
    {
        var fixture = await ProjectStructureFixture.CreateAsync();

        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        Assert.Contains(analysis.Diagnostics, x => x.Code == "project:discovery:fallback");
        Assert.Contains(analysis.Diagnostics, x => x.Code == "project:discovery:msbuild");
    }

    [Fact]
    public async Task Render_IsDeterministic_ForRepositorySolutionAndProjectPages()
    {
        var fixture = await ProjectStructureFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var renderer = new ProjectStructureWikiRenderer();

        var first = renderer.Render(model);
        var second = renderer.Render(model);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(x => x.RelativePath), second.Select(x => x.RelativePath));
        Assert.Equal(first.Select(x => x.Markdown), second.Select(x => x.Markdown));
        Assert.Equal(13, first.Count);
        Assert.Contains(first, x => x.RelativePath == "index/repository-index.md");
        Assert.Contains(first, x => x.RelativePath == "guidance/human.md");
        Assert.Contains(first, x => x.RelativePath == "guidance/llm-contract.md");

        var repositoryPage = first.Single(x => x.RelativePath == "repositories/sample-repo.md");
        Assert.Contains("## Guidance", repositoryPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[Human Guide](guidance/human.md)", repositoryPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[LLM Contract](guidance/llm-contract.md)", repositoryPage.Markdown, StringComparison.Ordinal);

        var indexPage = first.Single(x => x.RelativePath == "index/repository-index.md");
        Assert.Contains("## Guidance", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[Human Guide](guidance/human.md)", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[LLM Contract](guidance/llm-contract.md)", indexPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_LlmContract_ContainsNormativePoliciesAndGuardrails()
    {
        var fixture = await ProjectStructureFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var contract = pages.Single(x => x.RelativePath == "guidance/llm-contract.md");

        Assert.Contains("## Contract Rules", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("MUST", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("SHOULD", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Response Template", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("Summary", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("Evidence Links", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("Gaps/Risks", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("Next Queries", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Link Policy", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Evidence Policy", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Guardrails", contract.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_LlmContract_ContainsAnchoredRecipes_AndCapabilityMatrix()
    {
        var fixture = await ProjectStructureFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var pages = new ProjectStructureWikiRenderer().Render(model);
        var contract = pages.Single(x => x.RelativePath == "guidance/llm-contract.md");

        Assert.Contains("## Named Recipes", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("<a id=\"recipe-structure-survey\"></a>", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("<a id=\"recipe-hotspot-triage\"></a>", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("<a id=\"recipe-dependency-trace\"></a>", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Capability Matrix", contract.Markdown, StringComparison.Ordinal);

        var headBranchLine = contract.Markdown
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("head_branch:", StringComparison.Ordinal));
        var headBranch = headBranchLine["head_branch:".Length..].Trim();

        Assert.Contains($"[BL-013](https://github.com/vbfg1973/code-llm-wiki/blob/{headBranch}/plans/BACKLOG.md#bl-013-domain-term-extraction-and-linking)", contract.Markdown, StringComparison.Ordinal);
        Assert.Contains($"[BL-014](https://github.com/vbfg1973/code-llm-wiki/blob/{headBranch}/plans/BACKLOG.md#bl-014-endpoint-discovery-and-behavior-metadata)", contract.Markdown, StringComparison.Ordinal);
    }

    private sealed class ProjectStructureFixture
    {
        private ProjectStructureFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<ProjectStructureFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-{Guid.NewGuid():N}", "sample-repo");
            Directory.CreateDirectory(root);

            var solutionPath = Path.Combine(root, "Sample.slnx");
            var slnx = """
                       <Solution>
                         <Project Path="src/Valid/Valid.csproj" />
                         <Project Path="src/Broken/Broken.csproj" />
                       </Solution>
                       """;
            await File.WriteAllTextAsync(solutionPath, slnx);

            var validDir = Path.Combine(root, "src", "Valid");
            Directory.CreateDirectory(validDir);
            var validCsproj = """
                              <Project Sdk="Microsoft.NET.Sdk">
                                <PropertyGroup>
                                  <TargetFramework>net10.0</TargetFramework>
                                </PropertyGroup>
                              </Project>
                              """;
            await File.WriteAllTextAsync(Path.Combine(validDir, "Valid.csproj"), validCsproj);

            var brokenDir = Path.Combine(root, "src", "Broken");
            Directory.CreateDirectory(brokenDir);
            var brokenCsproj = """
                               <Project Sdk="Microsoft.NET.Sdk">
                                 <PropertyGroup>
                                   <TargetFramework>net10.0</TargetFramework>
                                 </PropertyGroup>
                                 <ItemGroup>
                               </Project>
                               """;
            await File.WriteAllTextAsync(Path.Combine(brokenDir, "Broken.csproj"), brokenCsproj);

            return new ProjectStructureFixture(root);
        }
    }
}
