using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class ReadWriteDataFlowVerticalSliceTests
{
    [Fact]
    public async Task Query_CapturesInternalPropertyAndFieldReadWriteEdges()
    {
        var fixture = await DataFlowFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var modelType = model.Declarations.Types.Single(x => x.Name == "Model");
        var workerType = model.Declarations.Types.Single(x => x.Name == "Worker");

        var nameMember = model.Declarations.Members.Single(x => x.DeclaringTypeId == modelType.Id && x.Name == "Name");
        var unusedMember = model.Declarations.Members.Single(x => x.DeclaringTypeId == modelType.Id && x.Name == "Unused");
        var counterMember = model.Declarations.Members.Single(x => x.DeclaringTypeId == modelType.Id && x.Name == "Counter");

        var readName = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "ReadName");
        var updateName = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "UpdateName");
        var bumpCounter = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "BumpCounter");
        var readExternalLength = model.Declarations.Methods.Declarations.Single(x => x.DeclaringTypeId == workerType.Id && x.Name == "ReadExternalLength");

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.ReadsProperty
            && x.SourceMethodId == readName.Id
            && x.TargetMemberId == nameMember.Id);

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.WritesProperty
            && x.SourceMethodId == updateName.Id
            && x.TargetMemberId == nameMember.Id);

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.ReadsField
            && x.SourceMethodId == bumpCounter.Id
            && x.TargetMemberId == counterMember.Id);

        Assert.Contains(model.Declarations.Methods.Relations, x =>
            x.Kind == MethodRelationKind.WritesField
            && x.SourceMethodId == bumpCounter.Id
            && x.TargetMemberId == counterMember.Id);

        Assert.DoesNotContain(model.Declarations.Methods.Relations, x => x.TargetMemberId == unusedMember.Id);
        Assert.DoesNotContain(model.Declarations.Methods.Relations, x => x.SourceMethodId == readExternalLength.Id && x.TargetMemberId is not null);
    }

    [Fact]
    public async Task Render_TypeAndMethodPages_IncludeDataFlowSectionsAndScalars()
    {
        var fixture = await DataFlowFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var modelTypePage = pages.Single(x =>
            x.RelativePath == "types/Acme/Data/Model.md");

        Assert.Contains("constructor_count: 0", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("method_count: 0", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("property_count: 2", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("field_count: 1", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("enum_member_count: 0", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("record_parameter_count: 0", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("behavioral_method_count: 0", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Member Data Flow", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("| Name | 1 | 1 |", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("| Unused | 0 | 0 | none | none |", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("| Counter | 1 | 1 |", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("ReadName", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("UpdateName", modelTypePage.Markdown, StringComparison.Ordinal);
        Assert.Contains("BumpCounter", modelTypePage.Markdown, StringComparison.Ordinal);

        var bumpCounterPage = pages.Single(x =>
            x.RelativePath.StartsWith("methods/Acme/Data/Worker/", StringComparison.Ordinal)
            && x.Markdown.Contains("method_signature: Acme.Data.Worker.BumpCounter()", StringComparison.Ordinal));

        Assert.Contains("## Reads", bumpCounterPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Writes", bumpCounterPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("field: Counter", bumpCounterPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class DataFlowFixture
    {
        private DataFlowFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<DataFlowFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-data-flow-{Guid.NewGuid():N}", "data-flow-repo");
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
                    <AssemblyName>Acme.Data.App</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(appDir, "Data.cs"),
                """
                namespace Acme.Data;

                public class Model
                {
                    public string Name { get; set; } = string.Empty;
                    public string Unused { get; set; } = string.Empty;
                    public int Counter;
                }

                public class Worker
                {
                    private readonly Model _model = new();

                    public string ReadName()
                    {
                        return _model.Name;
                    }

                    public void UpdateName(string value)
                    {
                        _model.Name = value;
                    }

                    public void BumpCounter()
                    {
                        _model.Counter++;
                    }

                    public int ReadExternalLength()
                    {
                        return string.Empty.Length;
                    }
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new DataFlowFixture(root);
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
