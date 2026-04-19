using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class TypeResolutionFallbackTests
{
    [Fact]
    public async Task Query_UsesResolvedIdentityLinks_WhenAvailable()
    {
        var fixture = await ResolutionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var internalDependency = model.Declarations.Types.Single(x => x.Name == "InternalDependency");
        var internalMember = model.Declarations.Members.Single(x => x.Name == "Internal" && x.Kind == MemberDeclarationKind.Property);

        Assert.NotNull(internalMember.DeclaredType);
        Assert.Equal(internalDependency.Id, internalMember.DeclaredType!.TypeId);
        Assert.Equal(DeclarationResolutionStatus.Resolved, internalMember.DeclaredType.ResolutionStatus);
    }

    [Fact]
    public async Task Query_UnresolvedDeclaredTypes_KeepSourceFallbackWithExplicitStatus()
    {
        var fixture = await ResolutionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var coordinates = model.Declarations.Members.Single(x => x.Name == "Coordinates" && x.Kind == MemberDeclarationKind.Property);

        Assert.NotNull(coordinates.DeclaredType);
        Assert.Null(coordinates.DeclaredType!.TypeId);
        Assert.Equal(DeclarationResolutionStatus.SourceTextFallback, coordinates.DeclaredType.ResolutionStatus);
        Assert.Contains("(int X, int Y)", coordinates.DeclaredType.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ExternalReferences_AreCapturedAsStubsWithoutStandalonePages()
    {
        var fixture = await ResolutionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var client = model.Declarations.Members.Single(x => x.Name == "Client" && x.Kind == MemberDeclarationKind.Property);
        Assert.NotNull(client.DeclaredType);
        Assert.Equal(DeclarationResolutionStatus.ExternalStub, client.DeclaredType!.ResolutionStatus);
        Assert.Equal("System.Net.Http.HttpClient", client.DeclaredType.DisplayText);

        var workerContract = model.Declarations.Types.Single(x => x.Name == "IWorkerContract");
        Assert.Contains(workerContract.DirectBaseTypes, x => x.DisplayText == "IDisposable" && x.ResolutionStatus == DeclarationResolutionStatus.ExternalStub);

        Assert.DoesNotContain(model.Declarations.Types, x => x.Name == "IDisposable" || x.Name == "HttpClient");
        Assert.DoesNotContain(pages, x => x.RelativePath.Contains("HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(pages, x => x.RelativePath.Contains("IDisposable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_PartialResolutionFailure_StillProducesUsableOutput_WithDiagnostics()
    {
        var fixture = await ResolutionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        Assert.Contains(analysis.Diagnostics, x => x.Code == "type:resolution:fallback");
        Assert.NotEmpty(model.Declarations.Types);
        Assert.NotEmpty(pages);
    }

    [Fact]
    public async Task Query_UnresolvedBaseTypes_AreRetainedWithFallbackStatus()
    {
        var fixture = await ResolutionFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var brokenWorker = model.Declarations.Types.Single(x => x.Name == "BrokenWorker");
        var unresolvedBase = Assert.Single(brokenWorker.DirectBaseTypes);

        Assert.Equal("Missing", unresolvedBase.DisplayText);
        Assert.Equal(DeclarationResolutionStatus.SourceTextFallback, unresolvedBase.ResolutionStatus);

        var brokenWorkerPage = pages.Single(x => x.RelativePath == "types/Acme/Resolution/BrokenWorker.md");
        Assert.Contains("Missing (source text fallback)", brokenWorkerPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class ResolutionFixture
    {
        private ResolutionFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<ResolutionFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-resolution-{Guid.NewGuid():N}", "resolution-repo");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "Resolution.cs"),
                """
                namespace Acme.Resolution;

                public class InternalDependency
                {
                }

                public interface IWorkerContract : IDisposable
                {
                }

                public class Worker : IWorkerContract
                {
                    public InternalDependency Internal { get; set; } = new();
                    public System.Net.Http.HttpClient Client { get; set; } = new();
                    public (int X, int Y) Coordinates { get; set; }

                    public void Dispose()
                    {
                    }
                }

                public class BrokenWorker : Missing[]
                {
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new ResolutionFixture(root);
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
