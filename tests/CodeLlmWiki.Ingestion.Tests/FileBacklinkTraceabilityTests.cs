using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class FileBacklinkTraceabilityTests
{
    [Fact]
    public async Task Render_TypePage_ListsAllDeclarationFiles_Deterministically()
    {
        var fixture = await TraceabilityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var typePage = pages.Single(x => x.RelativePath == "types/Trace/Sample/Shared.md");
        Assert.Contains("## Declaration Files", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("src/A/DeclarationsA.cs", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("src/B/DeclarationsB.cs", typePage.Markdown, StringComparison.Ordinal);

        var firstIndex = typePage.Markdown.IndexOf("src/A/DeclarationsA.cs", StringComparison.Ordinal);
        var secondIndex = typePage.Markdown.IndexOf("src/B/DeclarationsB.cs", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex);
    }

    [Fact]
    public async Task Render_FilePages_IncludeGroupedDeclarationBacklinksByKind()
    {
        var fixture = await TraceabilityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var filePage = pages.Single(x => x.RelativePath == "files/src/A/DeclarationsA.cs.md");
        Assert.Contains("## Declared Symbols", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Namespaces", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Types", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Members", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Methods", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Trace.Sample", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Shared", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("AValue", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("ZValue", filePage.Markdown, StringComparison.Ordinal);
        Assert.Contains(
            "- [[methods/Trace/Sample/Shared/Zed--no-params|Zed()]] (method)",
            filePage.Markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "- [[methods/Trace/Sample/Shared/Alpha--no-params|Alpha()]] (method)",
            filePage.Markdown,
            StringComparison.Ordinal);

        var zedIndex = filePage.Markdown.IndexOf(
            "- [[methods/Trace/Sample/Shared/Zed--no-params|Zed()]] (method)",
            StringComparison.Ordinal);
        var alphaIndex = filePage.Markdown.IndexOf(
            "- [[methods/Trace/Sample/Shared/Alpha--no-params|Alpha()]] (method)",
            StringComparison.Ordinal);
        Assert.True(zedIndex >= 0 && alphaIndex > zedIndex);
    }

    [Fact]
    public async Task Render_FilePageMemberBacklinks_OrderBySourceLocationThenIdentity()
    {
        var fixture = await TraceabilityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var filePage = pages.Single(x => x.RelativePath == "files/src/A/DeclarationsA.cs.md");
        var zValueIndex = filePage.Markdown.IndexOf("ZValue (property)", StringComparison.Ordinal);
        var aValueIndex = filePage.Markdown.IndexOf("AValue (property)", StringComparison.Ordinal);

        Assert.True(zValueIndex >= 0 && aValueIndex > zValueIndex);
    }

    [Fact]
    public async Task Render_BacklinkOrdering_IsDeterministic()
    {
        var fixture = await TraceabilityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var renderer = new ProjectStructureWikiRenderer();

        var first = renderer.Render(model);
        var second = renderer.Render(model);

        var firstPage = first.Single(x => x.RelativePath == "files/src/A/DeclarationsA.cs.md");
        var secondPage = second.Single(x => x.RelativePath == "files/src/A/DeclarationsA.cs.md");

        Assert.Equal(firstPage.Markdown, secondPage.Markdown);
    }

    [Fact]
    public async Task Render_TypeFrontMatter_UsesDeterministicPrimaryProjectContext()
    {
        var fixture = await TraceabilityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var typePage = pages.Single(x => x.RelativePath == "types/Trace/Sample/Shared.md");
        Assert.Contains("primary_project_name: AProject", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("primary_assembly_name: AProject", typePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("primary_project_path: src/A/A.csproj", typePage.Markdown, StringComparison.Ordinal);
    }

    private sealed class TraceabilityFixture
    {
        private TraceabilityFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<TraceabilityFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-traceability-{Guid.NewGuid():N}", "traceability-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/A/A.csproj" />
                  <Project Path="src/B/B.csproj" />
                </Solution>
                """);

            var projectADir = Path.Combine(root, "src", "A");
            var projectBDir = Path.Combine(root, "src", "B");
            Directory.CreateDirectory(projectADir);
            Directory.CreateDirectory(projectBDir);

            await File.WriteAllTextAsync(Path.Combine(projectADir, "A.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>AProject</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(projectBDir, "B.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>BProject</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(projectADir, "DeclarationsA.cs"),
                """
                namespace Trace.Sample;

                public partial class Shared
                {
                    public int ZValue { get; set; }
                    public int AValue { get; set; }
                    public void Zed() { }
                    public void Alpha() { }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(projectBDir, "DeclarationsB.cs"),
                """
                namespace Trace.Sample;

                public partial class Shared
                {
                    public int BValue { get; set; }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new TraceabilityFixture(root);
        }
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
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stdOut}\n{stdErr}");
        }
    }
}
