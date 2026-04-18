using System.Diagnostics;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class TypeSymbolVerticalSliceTests
{
    [Fact]
    public async Task AnalyzeAsync_IngestsAllApprovedTypeKinds_AndAccessibility()
    {
        var fixture = await TypeFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var types = model.Declarations.Types;
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Interface && x.Name == "IWorker");
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Class && x.Name == "BaseWorker");
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Record && x.Name == "WorkerRecord");
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Struct && x.Name == "WorkerStruct");
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Enum && x.Name == "WorkerStatus");
        Assert.Contains(types, x => x.Kind == TypeDeclarationKind.Delegate && x.Name == "WorkerDelegate");

        var internalType = types.Single(x => x.Name == "InternalWorker");
        Assert.Equal(DeclarationAccessibility.Internal, internalType.Accessibility);

        var publicType = types.Single(x => x.Name == "DerivedWorker");
        Assert.Equal(DeclarationAccessibility.Public, publicType.Accessibility);
    }

    [Fact]
    public async Task AnalyzeAsync_StoresDirectOnlyRelationshipEdges()
    {
        var fixture = await TypeFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var derived = model.Declarations.Types.Single(x => x.Name == "DerivedWorker");
        var baseType = model.Declarations.Types.Single(x => x.Name == "BaseWorker");
        var deeper = model.Declarations.Types.Single(x => x.Name == "DeeperWorker");
        var workerInterface = model.Declarations.Types.Single(x => x.Name == "IWorker");

        Assert.Contains(analysis.Triples, x =>
            x.Predicate == CorePredicates.Inherits &&
            x.Subject is EntityNode subject &&
            subject.Id == derived.Id &&
            x.Object is EntityNode obj &&
            obj.Id == baseType.Id);

        Assert.Contains(analysis.Triples, x =>
            x.Predicate == CorePredicates.Inherits &&
            x.Subject is EntityNode subject &&
            subject.Id == deeper.Id &&
            x.Object is EntityNode obj &&
            obj.Id == derived.Id);

        Assert.DoesNotContain(analysis.Triples, x =>
            x.Predicate == CorePredicates.Inherits &&
            x.Subject is EntityNode subject &&
            subject.Id == deeper.Id &&
            x.Object is EntityNode obj &&
            obj.Id == baseType.Id);

        Assert.Contains(analysis.Triples, x =>
            x.Predicate == CorePredicates.Implements &&
            x.Subject is EntityNode subject &&
            subject.Id == derived.Id &&
            x.Object is EntityNode obj &&
            obj.Id == workerInterface.Id);
    }

    [Fact]
    public async Task Query_ExposesDirectRelationships_AndNestingContext()
    {
        var fixture = await TypeFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var derived = model.Declarations.Types.Single(x => x.Name == "DerivedWorker");
        Assert.Equal(["BaseWorker"], derived.DirectBaseTypes.Select(x => x.DisplayText).ToArray());
        Assert.Equal(["IWorker"], derived.DirectInterfaceTypes.Select(x => x.DisplayText).ToArray());

        var outer = model.Declarations.Types.Single(x => x.Name == "Outer");
        var inner = model.Declarations.Types.Single(x => x.Name == "Inner");
        Assert.True(inner.IsNestedType);
        Assert.Equal(outer.Id, inner.DeclaringTypeId);
    }

    [Fact]
    public async Task Render_TypePages_IncludeIdentityAndDirectRelationshipSections()
    {
        var fixture = await TypeFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var derivedPage = pages.Single(x => x.RelativePath == "types/Acme/Workers/DerivedWorker.md");
        Assert.Contains("entity_type: type", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("type_name: DerivedWorker", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("type_kind: class", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("accessibility: public", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Direct Base Types", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("BaseWorker", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Direct Interfaces", derivedPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("IWorker", derivedPage.Markdown, StringComparison.Ordinal);

        var innerPage = pages.Single(x => x.RelativePath == "types/Acme/Workers/Outer/Inner.md");
        Assert.Contains("is_nested_type: true", innerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Nesting Context", innerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Outer", innerPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class TypeFixture
    {
        private TypeFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<TypeFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-types-{Guid.NewGuid():N}", "type-repo");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "Workers.cs"),
                """
                namespace Acme.Workers;

                public interface IWorker { }
                public class BaseWorker { }
                public class DerivedWorker : BaseWorker, IWorker { }
                public class DeeperWorker : DerivedWorker { }
                internal class InternalWorker { }
                public record WorkerRecord(string Name);
                public struct WorkerStruct { }
                public enum WorkerStatus { Unknown, Ready }
                public delegate void WorkerDelegate();
                public class Outer
                {
                    public class Inner : BaseWorker { }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new TypeFixture(root);
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
