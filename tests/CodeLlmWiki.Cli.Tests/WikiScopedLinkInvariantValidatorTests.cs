using System.Diagnostics;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Cli.Tests;

public sealed class WikiScopedLinkInvariantValidatorTests
{
    [Fact]
    public async Task Validate_ReturnsValid_WhenScopedSectionsContainWikiLinks()
    {
        var fixture = await ValidatorFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);
        var validator = new WikiScopedLinkInvariantValidator();

        var result = validator.Validate(new WikiScopedLinkInvariantValidationRequest(model, pages));

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task Validate_ReturnsConcreteViolations_WhenScopedSectionsContainPlainBullets()
    {
        var fixture = await ValidatorFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model)
            .Select(page =>
            {
                if (page.RelativePath == "namespaces/Sample/App.md")
                {
                    return page with
                    {
                        Markdown = page.Markdown.Replace(
                            "- [[types/Sample/App/Worker|Worker]] (class)",
                            "- Worker (class)",
                            StringComparison.Ordinal),
                    };
                }

                if (page.RelativePath == "files/src/App/Services.cs.md")
                {
                    var updated = page.Markdown
                        .Replace("- [[methods/Sample/App/Worker/Run--no-params|Run()]] (method)", "- Run() (method)", StringComparison.Ordinal)
                        .Replace("- [[methods/Sample/App/Worker/Load--int|Load(int)]] (method)", "- Load(int) (method)", StringComparison.Ordinal);
                    return page with { Markdown = updated };
                }

                return page;
            })
            .ToArray();

        var validator = new WikiScopedLinkInvariantValidator();

        var result = validator.Validate(new WikiScopedLinkInvariantValidationRequest(model, pages));

        Assert.False(result.IsValid);
        Assert.True(result.Violations.Count >= 2);
        Assert.Contains(result.Violations, x => x.PageRelativePath == "namespaces/Sample/App.md" && x.SectionPath == "## Contained Types" && x.LineNumber > 0);
        Assert.Contains(result.Violations, x => x.PageRelativePath == "files/src/App/Services.cs.md" && x.SectionPath == "## Declared Symbols > ### Methods" && x.LineNumber > 0);
    }

    private sealed class ValidatorFixture
    {
        private ValidatorFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<ValidatorFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-invariant-{Guid.NewGuid():N}", "fixture-repo");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "Services.cs"),
                """
                namespace Sample.App;

                public sealed class Worker
                {
                    public void Run() { }

                    public void Load(int count) { }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new ValidatorFixture(root);
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
