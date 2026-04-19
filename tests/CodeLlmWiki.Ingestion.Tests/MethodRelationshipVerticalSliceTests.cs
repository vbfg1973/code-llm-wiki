using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MethodRelationshipVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesImplementsAndOverridesMethodRelationships()
    {
        var fixture = await MethodRelationshipFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var interfaceType = model.Declarations.Types.Single(x => x.Name == "IWorker");
        var baseType = model.Declarations.Types.Single(x => x.Name == "BaseWorker");
        var implicitType = model.Declarations.Types.Single(x => x.Name == "ImplicitWorker");
        var explicitType = model.Declarations.Types.Single(x => x.Name == "ExplicitWorker");
        var overrideType = model.Declarations.Types.Single(x => x.Name == "OverrideWorker");

        var interfaceRun = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == interfaceType.Id && x.Name == "Run");
        var implicitRun = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == implicitType.Id && x.Name == "Run");
        var explicitRun = model.Declarations.Methods.Declarations.Single(x =>
            x.DeclaringTypeId == explicitType.Id
            && x.Name == "Run"
            && x.Signature.Contains(".IWorker.Run()", StringComparison.Ordinal));
        var baseExecute = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == baseType.Id && x.Name == "Execute");
        var overrideExecute = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == overrideType.Id && x.Name == "Execute");

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.ImplementsMethod &&
            x.SourceMethodId == implicitRun.Id &&
            x.TargetMethodId == interfaceRun.Id);

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.ImplementsMethod &&
            x.SourceMethodId == explicitRun.Id &&
            x.TargetMethodId == interfaceRun.Id);

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.OverridesMethod &&
            x.SourceMethodId == overrideExecute.Id &&
            x.TargetMethodId == baseExecute.Id);
    }

    [Fact]
    public async Task Render_MethodPages_IncludeImplementsAndOverridesSections()
    {
        var fixture = await MethodRelationshipFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var explicitMethodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Relationships/ExplicitWorker/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_signature: Acme.Relationships.ExplicitWorker.IWorker.Run()", StringComparison.Ordinal));
        Assert.Contains("## Implements", explicitMethodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("IWorker", explicitMethodPage.Markdown, StringComparison.Ordinal);

        var overrideMethodPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Relationships/OverrideWorker/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_signature: Acme.Relationships.OverrideWorker.Execute()", StringComparison.Ordinal));
        Assert.Contains("## Overrides", overrideMethodPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("BaseWorker", overrideMethodPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ResolvesOverrideRelationships_InMultiProjectContext()
    {
        var fixture = await MethodRelationshipProjectScopedFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var baseType = model.Declarations.Types.Single(x => x.Name == "BaseWorker");
        var overrideType = model.Declarations.Types.Single(x => x.Name == "OverrideWorker");
        var baseExecute = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == baseType.Id && x.Name == "Execute");
        var overrideExecute = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == overrideType.Id && x.Name == "Execute");

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.OverridesMethod &&
            x.SourceMethodId == overrideExecute.Id &&
            x.TargetMethodId == baseExecute.Id);

        Assert.DoesNotContain(analysis.Diagnostics, x => x.Code == "method:relationship:override:unresolved");
    }

    private sealed class MethodRelationshipFixture
    {
        private MethodRelationshipFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MethodRelationshipFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-method-rels-{Guid.NewGuid():N}", "method-rel-repo");
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
                    <AssemblyName>Acme.Relationships.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Relationships.cs"),
                """
                namespace Acme.Relationships;

                public interface IWorker
                {
                    void Run();
                }

                public class BaseWorker
                {
                    public virtual void Execute() { }
                }

                public class ImplicitWorker : IWorker
                {
                    public void Run() { }
                }

                public class ExplicitWorker : IWorker
                {
                    void IWorker.Run() { }
                }

                public class OverrideWorker : BaseWorker
                {
                    public override void Execute() { }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MethodRelationshipFixture(root);
        }
    }

    private sealed class MethodRelationshipProjectScopedFixture
    {
        private MethodRelationshipProjectScopedFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MethodRelationshipProjectScopedFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-method-rels-scoped-{Guid.NewGuid():N}", "method-rel-repo");
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"),
                """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                  <Project Path="src/BaseLib/BaseLib.csproj" />
                  <Project Path="src/ShadowLib/ShadowLib.csproj" />
                </Solution>
                """);

            var appDir = Path.Combine(root, "src", "App");
            var baseDir = Path.Combine(root, "src", "BaseLib");
            var shadowDir = Path.Combine(root, "src", "ShadowLib");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(shadowDir);

            await File.WriteAllTextAsync(Path.Combine(appDir, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.Relationships.App</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../BaseLib/BaseLib.csproj" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(baseDir, "BaseLib.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.Relationships.Base</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(shadowDir, "ShadowLib.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Acme.Relationships.Shadow</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(baseDir, "BaseWorker.cs"),
                """
                namespace Acme.Relationships;

                public class BaseWorker
                {
                    public virtual void Execute() { }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(shadowDir, "BaseWorker.cs"),
                """
                namespace Acme.Relationships;

                public class BaseWorker
                {
                    public virtual void Execute() { }
                }
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "OverrideWorker.cs"),
                """
                namespace Acme.Relationships;

                public class OverrideWorker : BaseWorker
                {
                    public override void Execute() { }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MethodRelationshipProjectScopedFixture(root);
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
