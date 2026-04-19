using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class TypeIdentityHardeningTests
{
    [Fact]
    public async Task Query_PartialDeclarations_AreCanonicalWithMultipleDeclarationFiles()
    {
        var fixture = await IdentityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var partialTypes = model.Declarations.Types.Where(x => x.Name == "PartialThing").ToArray();
        var partialType = Assert.Single(partialTypes);

        Assert.True(partialType.IsPartialType);
        Assert.Equal(2, partialType.DeclarationFileIds.Count);
    }

    [Fact]
    public async Task Query_GenericIdentity_CapturesArityParametersAndConstraints()
    {
        var fixture = await IdentityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var resultTypes = model.Declarations.Types
            .Where(x => x.Name == "Result")
            .OrderBy(x => x.Arity)
            .ToArray();

        Assert.Equal(2, resultTypes.Length);
        Assert.Equal(0, resultTypes[0].Arity);
        Assert.Equal(1, resultTypes[1].Arity);
        Assert.Equal(["T"], resultTypes[1].GenericParameters);
        Assert.Contains("T:class&new()", resultTypes[1].GenericConstraints);
    }

    [Fact]
    public async Task Render_TypePaths_AreReadableAndAmbiguityFree()
    {
        var fixture = await IdentityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        Assert.Contains(pages, x => x.RelativePath == "types/Acme/Identity/Result.md");
        Assert.Contains(pages, x => x.RelativePath == "types/Acme/Identity/Result-1.md");
        Assert.Contains(pages, x => x.RelativePath == "types/Acme/Identity/PartialThing.md");

        Assert.Equal(1, pages.Count(x => x.RelativePath == "types/Acme/Identity/PartialThing.md"));
    }

    [Fact]
    public async Task Render_NestedMetadata_IsConditional()
    {
        var fixture = await IdentityFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var nestedPage = pages.Single(x => x.RelativePath == "types/Acme/Identity/Container-1/Nested-1.md");
        Assert.Contains("is_nested_type: true", nestedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("declaring_type_id:", nestedPage.Markdown, StringComparison.Ordinal);

        var topLevelPage = pages.Single(x => x.RelativePath == "types/Acme/Identity/Container-1.md");
        Assert.DoesNotContain("is_nested_type: true", topLevelPage.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("declaring_type_id:", topLevelPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class IdentityFixture
    {
        private IdentityFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<IdentityFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-identity-{Guid.NewGuid():N}", "identity-repo");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "PartialThing.Part1.cs"),
                """
                namespace Acme.Identity;

                public partial class PartialThing
                {
                    public void Part1() { }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "PartialThing.Part2.cs"),
                """
                namespace Acme.Identity;

                public partial class PartialThing
                {
                    public void Part2() { }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Generics.cs"),
                """
                namespace Acme.Identity;

                public class Result { }

                public class Result<T>
                    where T : class, new()
                {
                }

                public class Container<TItem>
                    where TItem : Result, new()
                {
                    public class Nested<TNested>
                        where TNested : class
                    {
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new IdentityFixture(root);
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
