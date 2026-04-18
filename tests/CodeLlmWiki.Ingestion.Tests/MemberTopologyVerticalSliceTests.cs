using System.Diagnostics;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class MemberTopologyVerticalSliceTests
{
    [Fact]
    public async Task Query_IngestsMemberEntities_ForSupportedKinds()
    {
        var fixture = await MemberFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        Assert.Contains(model.Declarations.Members, x => x.Kind == MemberDeclarationKind.Property && x.Name == "Name");
        Assert.Contains(model.Declarations.Members, x => x.Kind == MemberDeclarationKind.Field && x.Name == "_count");
        Assert.Contains(model.Declarations.Members, x => x.Kind == MemberDeclarationKind.EnumMember && x.Name == "Ready");
        Assert.Contains(model.Declarations.Members, x => x.Kind == MemberDeclarationKind.RecordParameter && x.Name == "Id");
    }

    [Fact]
    public async Task Query_CapturesDeclaredMemberTypeRelationships()
    {
        var fixture = await MemberFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);

        var helperType = model.Declarations.Types.Single(x => x.Name == "Helper");
        var helperProperty = model.Declarations.Members.Single(x => x.Name == "Helper" && x.Kind == MemberDeclarationKind.Property);

        Assert.NotNull(helperProperty.DeclaredType);
        Assert.Equal(helperType.Id, helperProperty.DeclaredType!.TypeId);
        Assert.Equal(DeclarationResolutionStatus.Resolved, helperProperty.DeclaredType.ResolutionStatus);
    }

    [Fact]
    public async Task Render_TypePages_ContainDeterministicMemberSections_AndNoStandaloneMemberPages()
    {
        var fixture = await MemberFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        Assert.DoesNotContain(pages, x => x.RelativePath.StartsWith("members/", StringComparison.Ordinal));

        var workerPage = pages.Single(x => x.RelativePath == "types/Acme/Members/Worker.md");
        Assert.Contains("## Members", workerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Properties", workerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Fields", workerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Name", workerPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("_count", workerPage.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_EnumMembers_IncludeConstantValues()
    {
        var fixture = await MemberFixture.CreateAsync();
        var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
        var analysis = await analyzer.AnalyzeAsync(fixture.RepositoryPath, CancellationToken.None);
        var model = new ProjectStructureQueryService(analysis.Triples).GetModel(analysis.RepositoryId);
        var pages = new ProjectStructureWikiRenderer().Render(model);

        var enumPage = pages.Single(x => x.RelativePath == "types/Acme/Members/Status.md");
        Assert.Contains("### Enum Members", enumPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Unknown = 0", enumPage.Markdown, StringComparison.Ordinal);
        Assert.Contains("Ready = 1", enumPage.Markdown, StringComparison.Ordinal);
    }

    private sealed class MemberFixture
    {
        private MemberFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async Task<MemberFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"codellmwiki-members-{Guid.NewGuid():N}", "member-repo");
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

            await File.WriteAllTextAsync(Path.Combine(appDir, "Members.cs"),
                """
                namespace Acme.Members;

                public class Helper
                {
                }

                public record Job(Guid Id, Helper Helper);

                public class Worker
                {
                    public string Name { get; set; } = string.Empty;
                    private readonly int _count = 3;
                    public Helper Helper { get; set; } = new();
                }

                public enum Status
                {
                    Unknown,
                    Ready
                }
                """);

            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test User");
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "initial");

            return new MemberFixture(root);
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
