using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class FileInventoryVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_RepresentsAllGitTrackedHeadFilesInGraph()
    {
        var fixture = await FileInventoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);

        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        Assert.Equal(5, model.Files.Count);
        Assert.Contains(model.Files, x => x.Path == "Sample.slnx");
        Assert.Contains(model.Files, x => x.Path == "src/App/App.csproj");
        Assert.Contains(model.Files, x => x.Path == "src/App/Program.cs");
        Assert.Contains(model.Files, x => x.Path == "README.md");
        Assert.Contains(model.Files, x => x.Path == "appsettings.json");
    }

    [Fact]
    public async Task AnalyzeAsync_FileClassificationFactsAreQueryable()
    {
        var fixture = await FileInventoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var source = model.Files.Single(x => x.Path == "src/App/Program.cs");
        Assert.Equal("dotnet-source", source.Classification);
        Assert.True(source.IsSolutionMember);

        var readme = model.Files.Single(x => x.Path == "README.md");
        Assert.Equal("documentation", readme.Classification);
        Assert.False(readme.IsSolutionMember);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesHeadTrackedBoundary_ForBuildArtifacts()
    {
        var fixture = await FileInventoryFixture.CreateAsync();

        var objDir = Path.Combine(fixture.RepositoryPath, "src", "App", "obj");
        var binDir = Path.Combine(fixture.RepositoryPath, "src", "App", "bin", "Debug");
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(binDir);

        var trackedBuildArtifactPath = Path.Combine(objDir, "tracked.assets.json");
        var untrackedBuildArtifactPath = Path.Combine(binDir, "untracked.dll");

        await File.WriteAllTextAsync(trackedBuildArtifactPath, "{ \"tracked\": true }");
        await File.WriteAllTextAsync(untrackedBuildArtifactPath, "binary");

        RunGit(fixture.RepositoryPath, "add", "src/App/obj/tracked.assets.json");
        RunGit(fixture.RepositoryPath, "commit", "-m", "track one build artifact at HEAD");

        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        Assert.Contains(model.Files, x => x.Path == "src/App/obj/tracked.assets.json");
        Assert.DoesNotContain(model.Files, x => x.Path == "src/App/bin/Debug/untracked.dll");
    }

    [Fact]
    public async Task Render_FilePagesAreMetadataOnlyWithMinimalFrontMatter()
    {
        var fixture = await FileInventoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var renderer = new ProjectStructureWikiRenderer();
        var pages = renderer.Render(model);

        var filePages = pages
            .Where(x => x.RelativePath.StartsWith("files/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(5, filePages.Length);

        var programPage = filePages.Single(x => x.Markdown.Contains("src/App/Program.cs", StringComparison.Ordinal));
        Assert.Contains(filePages, x => x.RelativePath == "files/src/App/Program.cs.md");
        Assert.DoesNotContain(filePages, x => x.RelativePath.Contains("file-", StringComparison.Ordinal));
        Assert.StartsWith("---", programPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("entity_type: file", programPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("classification: dotnet-source", programPage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("HELLO FROM SOURCE", programPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_GeneratesCanonicalRepositoryIndexWithEntityMappings()
    {
        var fixture = await FileInventoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var renderer = new ProjectStructureWikiRenderer();
        var pages = renderer.Render(model);

        var indexPage = pages.Single(x => x.RelativePath == "index/repository-index.md");
        Assert.Contains("| name | path | entity_id | page_link |", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Projects", indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Files", indexPage.Markdown, StringComparison.Ordinal);

        var project = model.Projects.Single();
        Assert.Contains(project.Id.Value, indexPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[App](projects/App.md)", indexPage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| [[", indexPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_EmitsRepositoryIdInFrontMatterForAllPages()
    {
        var fixture = await FileInventoryFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var query = new ProjectStructureQueryService(analysis.Triples);
        var model = query.GetModel(analysis.RepositoryId);

        var renderer = new ProjectStructureWikiRenderer();
        var pages = renderer.Render(model);

        Assert.All(
            pages,
            page => Assert.Contains(
                $"repository_id: {analysis.RepositoryId.Value}",
                page.Markdown,
                StringComparison.Ordinal));
    }

    private sealed class FileInventoryFixture
    {
        private FileInventoryFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<FileInventoryFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-files-{Guid.NewGuid():N}", "file-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"), "Console.WriteLine(\"HELLO FROM SOURCE\");");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# sample");
            await File.WriteAllTextAsync(Path.Combine(root, "appsettings.json"), "{ \"Name\": \"Sample\" }");

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new FileInventoryFixture(root);
        }

        private static void RunGit(string workingDirectory, params string[] args)
        {
            FileInventoryVerticalSliceTests.RunGit(workingDirectory, args);
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
