using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MethodDeclarationVerticalSliceTests
{
    [Fact]
    public async Task Query_IngestsMethodsAndConstructors_IncludingBodylessDeclarations()
    {
        var fixture = await MethodFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var workerType = model.Declarations.Types.Single(x => x.Name == "Worker");
        var interfaceType = model.Declarations.Types.Single(x => x.Name == "IWorker");
        var baseType = model.Declarations.Types.Single(x => x.Name == "BaseWorker");

        var workerMethods = model.Declarations.Methods.Declarations
            .Where(x => x.DeclaringTypeId == workerType.Id)
            .ToArray();
        var interfaceMethods = model.Declarations.Methods.Declarations
            .Where(x => x.DeclaringTypeId == interfaceType.Id)
            .ToArray();
        var baseMethods = model.Declarations.Methods.Declarations
            .Where(x => x.DeclaringTypeId == baseType.Id)
            .ToArray();

        Assert.Equal(2, workerMethods.Count(x => x.Kind == MethodDeclarationKind.Constructor));
        Assert.Contains(workerMethods, x => x.Kind == MethodDeclarationKind.Method && x.Name == "Work");
        Assert.Contains(workerMethods, x => x.Kind == MethodDeclarationKind.Method && x.Name == "Compute");
        Assert.Contains(workerMethods, x => x.Kind == MethodDeclarationKind.Method && x.Name == "Native");
        Assert.Contains(interfaceMethods, x => x.Kind == MethodDeclarationKind.Method && x.Name == "Work");
        Assert.Contains(baseMethods, x => x.Kind == MethodDeclarationKind.Method && x.Name == "Compute");

        var compute = Assert.Single(workerMethods, x => x.Name == "Compute");
        Assert.NotNull(compute.ReturnType);
        Assert.Single(compute.Parameters);
        Assert.NotEmpty(compute.DeclarationFileIds);
        Assert.NotEmpty(compute.DeclarationLocations);

        var ctorWithName = Assert.Single(workerMethods, x => x.Kind == MethodDeclarationKind.Constructor && x.Parameters.Count == 1);
        Assert.Null(ctorWithName.ReturnType);
        Assert.Equal("name", ctorWithName.Parameters[0].Name);
    }

    [Fact]
    public async Task Render_EmitsMethodPages_AndLinksFromOwningTypePage()
    {
        var fixture = await MethodFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var workerPage = pages.Single(x => x.RelativePath == "types/Acme/Methods/Worker.md");
        Assert.Contains("## Methods", workerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("[[methods/Acme/Methods/Worker/", workerPage.Markdown, StringComparison.Ordinal);

        var methodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Methods/Worker/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_name: Compute", StringComparison.Ordinal));

        Assert.Contains("entity_type: method", methodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("method_kind: method", methodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("declaring_type_name: Worker", methodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Signature", methodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Parameters", methodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Declaration Files", methodPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class MethodFixture
    {
        private MethodFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MethodFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-methods-{Guid.NewGuid():N}", "method-repo");
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
                    <AssemblyName>Acme.Methods.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Methods.cs"),
                """
                namespace Acme.Methods;

                public interface IWorker
                {
                    void Work(string input);
                }

                public abstract class BaseWorker
                {
                    public abstract int Compute(int value);
                }

                public class Worker : BaseWorker, IWorker
                {
                    public Worker() { }

                    public Worker(string name) { }

                    public override int Compute(int value) => value;

                    public void Work(string input) { }

                    public extern void Native();
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MethodFixture(root);
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
