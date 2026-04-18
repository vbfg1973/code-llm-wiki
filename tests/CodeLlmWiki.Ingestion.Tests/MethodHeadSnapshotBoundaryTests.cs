using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MethodHeadSnapshotBoundaryTests
{
    [Fact]
    public async Task Analyze_UsesHeadSnapshotForMethodAndDataFlow_WhenWorkingTreeIsDirty()
    {
        var fixture = await HeadSnapshotFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var workerType = model.Declarations.Types.Single(x => x.Name == "Worker");
        var valueMember = model.Declarations.Members.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "Value");
        var getMethod = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "Get");

        Assert.DoesNotContain(
            model.Declarations.Methods.Declarations,
            x => x.DeclaringTypeId == workerType.Id && x.Name == "DirtyOnlyInWorkingTree");

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.ReadsField
            && x.SourceMethodId == getMethod.Id
            && x.TargetMemberId == valueMember.Id);

        Assert.Contains(analysis.Diagnostics, x => x.Code == "repository:dirty-working-tree");
    }

    private sealed class HeadSnapshotFixture
    {
        private HeadSnapshotFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<HeadSnapshotFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-head-snapshot-{Guid.NewGuid():N}", "head-snapshot-repo");
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
                    <AssemblyName>Acme.Head.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var sourcePath = Path.Combine(appDir, "Worker.cs");
            await File.WriteAllTextAsync(sourcePath,
                """
                namespace Acme.Head;

                public class Worker
                {
                    public int Value;

                    public int Get()
                    {
                        return Value;
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            await File.WriteAllTextAsync(sourcePath,
                """
                namespace Acme.Head;

                public class Worker
                {
                    public int Value;

                    public int Get()
                    {
                        return 99;
                    }

                    public int DirtyOnlyInWorkingTree()
                    {
                        return Value;
                    }
                }
                """);

            return new HeadSnapshotFixture(root);
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
